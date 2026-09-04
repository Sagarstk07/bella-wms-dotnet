using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IToteTransactionRepository"/> over
/// <c>PUB.transactions</c>.
/// </summary>
/// <remarks>
/// Columns verified against <c>schema/irms.df</c>. The query goes through
/// <c>co_wh_trans_link_id</c> (co_num, wh_num, trans_type, link_id, row_status), which is
/// exactly this access pattern — the index exists because the ABL does this a lot.
/// </remarks>
public sealed class ToteTransactionRepository : IToteTransactionRepository
{
    // ⚠ CASE. trans_type and row_status are single/double-character columns compared
    // case-insensitively by the ABL. See the ground rules in CLAUDE.md.
    private const string OpenToteFilter = """
        co_num     = @company
        AND wh_num     = @warehouse
        AND trans_type IN ('JT', 'jt')
        AND link_id    = @toteId
        AND (row_status IS NULL OR row_status NOT IN ('C', 'c'))
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<ToteTransactionRepository> _logger;

    public ToteTransactionRepository(
        IIrmsUnitOfWork unitOfWork,
        ILogger<ToteTransactionRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ToteTransaction?> FindOpenByToteAsync(
        string company,
        string warehouse,
        string toteId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toteId))
        {
            return null;
        }

        var sql = $"""
            SELECT carton_id AS CartonId,
                   void      AS IsVoid
            FROM   PUB.transactions
            WHERE  {OpenToteFilter}
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<ToteTransaction>(
            new CommandDefinition(
                sql,
                new { company, warehouse, toteId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var open = rows.ToList();

        if (open.Count > 1)
        {
            // The ABL detects this by running the query twice and checking whether the
            // second, non-"first" form errors — locusAPI.cls:2255. Its comment says
            // multiple pending JTs exist "after PICKCOMPLETE". Worth logging: it means a
            // tote is associated with more than one carton at once.
            _logger.LogWarning(
                "{Count} open JT rows for tote {ToteId} in {Company}/{Warehouse}. " +
                "The tote is in use by more than one carton.",
                open.Count, toteId, company, warehouse);
        }

        return open.Count == 0 ? null : open[0];
    }

    /// <inheritdoc />
    public async Task<int> CloseOpenAsync(
        string company,
        string warehouse,
        string toteId,
        string? cartonId,
        string comments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comments);

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "CloseOpenAsync must run inside a unit of work. The ABL takes an " +
                "EXCLUSIVE-LOCK on each transactions row for this write.");
        }

        var cartonFilter = cartonId is null ? string.Empty : "AND carton_id = @cartonId";

        var sql = $"""
            UPDATE PUB.transactions
            SET    row_status = 'C',
                   comments   = @comments
            WHERE  {OpenToteFilter}
            {cartonFilter}
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { company, warehouse, toteId, cartonId, comments },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        _logger.LogDebug(
            "Closed {Count} JT rows for tote {ToteId} in {Company}/{Warehouse}.",
            affected, toteId, company, warehouse);

        return affected;
    }

    /// <inheritdoc />
    public async Task<int> RelinkOpenByOrderAsync(
        string company,
        string warehouse,
        string order,
        string orderSuffix,
        string newToteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newToteId);

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "RelinkOpenByOrderAsync must run inside a unit of work. The ABL takes an " +
                "EXCLUSIVE-LOCK on each transactions row for this write.");
        }

        // WARNING: row_status = 'O' exactly, not <> 'C'. locusAPI.cls:2452 narrows it here
        // and nowhere else, so a JT row still at 'P' is deliberately left behind.
        const string sql = """
            UPDATE PUB.transactions
            SET    link_id = @newToteId
            WHERE  co_num     = @company
            AND    wh_num     = @warehouse
            AND    po_number  = @order
            AND    po_suffix  = @orderSuffix
            AND    trans_type IN ('JT', 'jt')
            AND    row_status IN ('O', 'o')
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { newToteId, company, warehouse, order, orderSuffix },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        _logger.LogDebug(
            "Relinked {Count} JT rows for order {Order}-{Suffix} to tote {ToteId}.",
            affected, order, orderSuffix, newToteId);

        return affected;
    }
}
