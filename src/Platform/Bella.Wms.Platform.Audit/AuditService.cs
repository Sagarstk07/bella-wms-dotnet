using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Audit;

/// <summary>
/// Dapper-backed append-only implementation of <see cref="IAuditService"/>.
/// </summary>
public sealed class AuditService : IAuditService
{
    // VERIFIED against the irms .df dump, 2026-09-03. Every column below exists on
    // PUB.transactions with the type this class binds. Three defects were corrected at
    // that verification and are noted here so they are not reintroduced:
    //
    //   1. `comment` was in the column list. There is no such column — the table has
    //      `comments` and `ns_comment`. `comment` is a separate TABLE in irms. The insert
    //      would have failed on the first audit row ever written.
    //   2. `custom_data` was bound as a scalar. It is declared EXTENT 5 — a Progress
    //      array. Over SQL-92 it surfaces as custom_data##1..##5, so a scalar bind cannot
    //      work. Now written as five bound parameters — replenputcomplete needs slots 1
    //      and 4 (locusAPI.cls:3550, 3553). See AuditTransaction.CustomData.
    //   3. `item_type` was absent and is MANDATORY with INITIAL "S". ABL CREATE applies
    //      that default; a SQL INSERT does not, so the row would violate the constraint.
    //      Now written explicitly.
    //
    // The same INITIAL problem applies to `row_status` ("O" = open) and `void` (no).
    // 795 fields across 121 tables carry ABL defaults that a SQL INSERT will not apply —
    // see docs/SCHEMA_REVIEW.md. Treat every INSERT in this solution as suspect until it
    // has been checked against that list.
    //
    // NOTE ON PARAMETER BINDING: the OpenEdge ODBC driver may bind positionally with '?'
    // rather than by name. Dapper emits named parameters. Still unresolved — open item 4.
    private const string InsertSql = """
        INSERT INTO PUB.transactions
            (trans_num, co_num, wh_num, trans_type, item_type, emp_num, carton_id, po_number,
             po_suffix, abs_num, bin_num, bin_from, bin_to, lot, sugg_qty, item_qty,
             case_qty, link_id, doc_id, task_id, pallet_id, pallet_id_from, lpn_scanid,
             comments, ns_comment, row_status, void, dept_num, shf_num, date_time,
             trans_sec_time, proc_created, batch, stock_stat, old_stock_stat, adj_code,
             mach_type,
             "custom_data##1", "custom_data##2", "custom_data##3", "custom_data##4",
             "custom_data##5")
        VALUES
            (@TransactionNumber, @Company, @Warehouse, @TransactionType, @ItemType, @EmployeeNumber,
             @CartonId, @OrderNumber, @OrderSuffix, @ItemNumber, @BinNumber, @BinFrom,
             @BinTo, @Lot, @SuggestedQuantity, @ItemQuantity, @CaseQuantity, @LinkId,
             @DocumentId, @TaskId, @PalletId, @PalletIdFrom, @LpnScanId,
             @Comments, @NsComment, @RowStatus, @IsVoid, @DepartmentNumber, @ShiftNumber,
             @DateTime, @TransSecTime, @ProcCreated, @Batch, @StockStatus,
             @OldStockStatus, @AdjustmentCode, @MachType,
             @CustomData1, @CustomData2, @CustomData3, @CustomData4, @CustomData5)
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly ISequenceAllocator _sequences;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ISequenceAllocator sequences,
        ILogger<AuditService> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<long>> AppendAsync(
        AuditTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!_unitOfWork.IsActive)
        {
            // An audit row that commits independently of the change it records can
            // survive a rollback and describe something that never happened.
            return OperationResult<long>.Failure(
                "Audit rows must be written inside the caller's unit of work. " +
                "Open a transaction before calling AppendAsync.",
                "NO_ACTIVE_TRANSACTION");
        }

        var row = Enrich(transaction);

        try
        {
            // trans_num is MANDATORY with no default and a UNIQUE index, and its sequence
            // cycles — so the number has to be drawn and checked, inside this transaction,
            // before the insert. Converts trigger/n_trans.t:20-25. 51 of the 55 sequences
            // in this database behave the same way, which is why the allocator is general.
            row = row with
            {
                TransactionNumber = row.TransactionNumber
                    ?? await _sequences.NextAsync(
                        "transactions_trans_num", "transactions", "trans_num", cancellationToken)
                        .ConfigureAwait(false),
            };

            var command = new CommandDefinition(
                InsertSql,
                row,
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken);

            await _unitOfWork.Connection.ExecuteAsync(command).ConfigureAwait(false);

            _logger.LogDebug(
                "Audit {TransType} written for {Company}/{Warehouse} carton {CartonId}.",
                row.TransactionType, row.Company, row.Warehouse, row.CartonId);

            return OperationResult<long>.Success(row.TransactionNumber!.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit row {TransType} for {Company}/{Warehouse}.",
                row.TransactionType, row.Company, row.Warehouse);

            return OperationResult<long>.Failure(
                $"Audit write failed: {ex.Message}", "AUDIT_WRITE_FAILED");
        }
    }

    public async Task<OperationResult> AppendManyAsync(
        IReadOnlyCollection<AuditTransaction> transactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        if (transactions.Count == 0)
        {
            return OperationResult.Success();
        }

        foreach (var transaction in transactions)
        {
            var result = await AppendAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (result.Failed)
            {
                return result.WithoutValue();
            }
        }

        return OperationResult.Success();
    }

    /// <summary>
    /// Fills in fields the caller does not have to supply: <see cref="AuditTransaction.DepartmentNumber"/>
    /// and <see cref="AuditTransaction.ShiftNumber"/> from ambient <c>static_connect</c>
    /// (<c>wsMiddlewareAPI.p:311-315</c>), and <see cref="AuditTransaction.DateTime"/> /
    /// <see cref="AuditTransaction.TransSecTime"/> the way <c>n_trans.t</c> does — both
    /// captured from one instant, since the ABL derives them from the same
    /// <c>sdate</c>/<c>stime</c>/<c>TIME</c> capture at CREATE time (<c>docs/TRIGGER_RULES.md</c>).
    /// </summary>
    private AuditTransaction Enrich(AuditTransaction transaction) =>
        transaction.Enrich(
            _context.DepartmentNumber,
            _context.ShiftNumber,
            _context.OriginStack,
            DateTimeOffset.Now);
}
