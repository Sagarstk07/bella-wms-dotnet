using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="ICartonRepository"/> against <c>cartonmst</c> in
/// <c>irms</c>.
/// </summary>
/// <remarks>
/// <b>SCHEMA-INFERRED.</b> The 21 columns were harvested from every <c>cartonmst.*</c>
/// reference in <c>api/wms</c>; <c>cartonmst</c> is the table this module touches most
/// (29 references). Verify against the <c>irms</c> <c>.df</c> before first run.
/// </remarks>
public sealed class CartonRepository : ICartonRepository
{
    private const string SelectColumns = """
        co_num           AS Company,
        wh_num           AS Warehouse,
        carton_id        AS CartonId,
        carton_num       AS CartonNumber,
        "order"          AS "Order",
        order_suffix     AS OrderSuffix,
        bin_num          AS BinNumber,
        box_id           AS BoxId,
        batch            AS Batch,
        carrier_id       AS CarrierId,
        tracking_id      AS TrackingId,
        weight           AS Weight,
        length           AS Length,
        width            AS Width,
        height           AS Height,
        row_status       AS RowStatus,
        external_status  AS ExternalStatus,
        external_id      AS ExternalId,
        external_message AS ExternalMessage
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<CartonRepository> _logger;

    public CartonRepository(IIrmsUnitOfWork unitOfWork, ILogger<CartonRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <remarks>
    /// <para>
    /// Converts <c>find first cartonmst where co_num / wh_num / carton_id no-lock</c> —
    /// the pattern repeated throughout <c>locusAPI.cls</c>, for example lines 1767-1772.
    /// </para>
    /// <para>
    /// Reads at <c>READ UNCOMMITTED</c> outside a transaction, matching ABL
    /// <c>NO-LOCK</c>. Inside a caller's transaction it inherits that transaction's
    /// isolation, which is the behaviour a write path wants.
    /// </para>
    /// <para>
    /// <b>Note on <c>"order"</c>.</b> The ABL field is literally named <c>order</c>,
    /// which is a reserved word in SQL. It is quoted here. Whether the OpenEdge SQL layer
    /// accepts double quotes or requires brackets is one of the things the first
    /// connection test will settle — flagged in <c>docs/SCHEMA_REVIEW.md</c>.
    /// </para>
    /// </remarks>
    public async Task<Carton?> FindByCartonIdAsync(
        string company,
        string warehouse,
        string cartonId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cartonId))
        {
            return null;
        }

        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.cartonmst
            WHERE  co_num    = @company
            AND    wh_num    = @warehouse
            AND    carton_id = @cartonId
            """;

        return await _unitOfWork.Connection.QueryFirstOrDefaultAsync<Carton>(
            new CommandDefinition(
                sql,
                new { company, warehouse, cartonId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <remarks>
    /// <para>
    /// Converts the <c>external_status</c> / <c>external_id</c> assignments the
    /// AutoBagger classes perform, for example <c>accutechAutoBaggerOut.cls</c> when a
    /// carton moves through <c>GetZPL</c> → <c>SendZPL</c> → <c>OutWithZPL</c>.
    /// </para>
    /// <para>
    /// The caller must have an open transaction. The ABL takes an <c>EXCLUSIVE-LOCK</c>
    /// on <c>cartonmst</c> to do this, and doing it outside a transaction here would
    /// commit a status change independently of whatever business step it belongs to.
    /// </para>
    /// </remarks>
    public async Task UpdateExternalStatusAsync(
        string company,
        string warehouse,
        string cartonId,
        string externalStatus,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "UpdateExternalStatusAsync must run inside a unit of work. The ABL takes " +
                "an EXCLUSIVE-LOCK on cartonmst for this write.");
        }

        const string sql = """
            UPDATE PUB.cartonmst
            SET    external_status = @externalStatus,
                   external_id     = @externalId
            WHERE  co_num          = @company
            AND    wh_num          = @warehouse
            AND    carton_id       = @cartonId
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { externalStatus, externalId, company, warehouse, cartonId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            // The ABL would have found no record and fallen through a NO-ERROR without
            // saying anything. Silence here would hide a carton that vanished mid-flow.
            _logger.LogError(
                "No cartonmst row updated for {Company}/{Warehouse} carton {CartonId}.",
                company, warehouse, cartonId);
        }
        else if (affected > 1)
        {
            _logger.LogError(
                "{Count} cartonmst rows updated for {Company}/{Warehouse} carton {CartonId}. " +
                "The carton key is not unique — confirm the index in the .df.",
                affected, company, warehouse, cartonId);
        }
    }

    /// <remarks>
    /// <para>
    /// Converts the reject family's re-find <c>EXCLUSIVE-LOCK</c> plus conditional
    /// <c>row_status</c> write into one atomic statement — see the interface remarks for
    /// why a single guarded <c>UPDATE</c> stands in for the ABL's two-step version.
    /// </para>
    /// <para>
    /// The caller must have an open transaction, for the same reason as
    /// <see cref="UpdateExternalStatusAsync"/>.
    /// </para>
    /// </remarks>
    public async Task MarkErrorUnlessCompleteAsync(
        string company,
        string warehouse,
        string cartonId,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "MarkErrorUnlessCompleteAsync must run inside a unit of work. The ABL " +
                "takes an EXCLUSIVE-LOCK on cartonmst for this write.");
        }

        // ⚠ CASE. The ABL comparison this converts is case-INSENSITIVE — that is
        // Progress's default, and exactly 1 of the 2,431 fields in irms.df opts out of it.
        // SQL-92 comparison is case-SENSITIVE. The codebase demonstrably writes both cases
        // of the same status: pick_status has INITIAL "O" and is queried as "o" in six
        // places across four modules (locusAPI.cls:2300 among them).
        //
        // So `row_status <> 'C'` would mark a carton as errored that the ABL considers
        // complete, purely because the stored value is lowercase. Every character
        // comparison in this solution needs the same treatment — see
        // docs/SCHEMA_REVIEW.md item 11.
        //
        // NOT IN ('C','c') rather than UPPER(row_status) <> 'C' because the function form
        // cannot use the index.
        const string sql = """
            UPDATE PUB.cartonmst
            SET    row_status = 'E'
            WHERE  co_num     = @company
            AND    wh_num     = @warehouse
            AND    carton_id  = @cartonId
            AND    (row_status IS NULL OR row_status NOT IN ('C', 'c'))
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { company, warehouse, cartonId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected > 1)
        {
            _logger.LogError(
                "{Count} cartonmst rows updated for {Company}/{Warehouse} carton {CartonId}. " +
                "The carton key is not unique — but the .df declares co_wh_id UNIQUE on " +
                "(co_num, wh_num, carton_id), so this should be impossible.",
                affected, company, warehouse, cartonId);
        }

        // affected == 0 is expected and benign: row_status was already 'C', or the
        // carton vanished between the caller's earlier NO-LOCK read and this write —
        // either way there is nothing to correct here.
    }

    /// <inheritdoc />
    public async Task<bool> IsSingleUnitOrderAsync(
        string company,
        string warehouse,
        string order,
        string orderSuffix,
        CancellationToken cancellationToken = default)
    {
        // One row per carton on the order, with how many cartondtl lines it has and the
        // largest quantity among them. The ABL walks the same set with a nested
        // find — grouping here gets the same facts in one round trip.
        const string sql = """
            SELECT c.carton_num       AS CartonNumber,
                   COUNT(d.carton_num) AS DetailCount,
                   MAX(d.qty)          AS MaxQuantity
            FROM   PUB.cartonmst c
            LEFT JOIN PUB.cartondtl d ON d.carton_num = c.carton_num
            WHERE  c.co_num       = @company
            AND    c.wh_num       = @warehouse
            AND    c."order"      = @order
            AND    c.order_suffix = @orderSuffix
            GROUP BY c.carton_num
            """;

        var cartons = (await _unitOfWork.Connection.QueryAsync<CartonDetailSummary>(
            new CommandDefinition(
                sql,
                new { company, warehouse, order, orderSuffix },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        // The ABL's count test is `if iCartonCount gt 1` evaluated BEFORE the increment,
        // so it only bails on the third carton — two cartons still count as "single unit".
        // Suspected defect, reproduced deliberately. See IsSingleUnitOrderAsync on
        // ICartonRepository.
        if (cartons.Count > 2)
        {
            return false;
        }

        // `find cartondtl where carton_num eq X` with no FIRST raises an ambiguity error
        // when a carton has more than one detail line, and NO-ERROR then leaves the buffer
        // unavailable — which the ABL treats exactly like "not found". cartondtl's primary
        // index is (carton_num, abs_num), so multiple lines per carton are normal, and a
        // multi-line carton therefore disqualifies the order. That is easy to miss and
        // easy to "fix" into FIRST, which would change behaviour.
        return cartons.All(c => c.DetailCount == 1 && (c.MaxQuantity ?? 0m) <= 1m);
    }

    /// <inheritdoc />
    public async Task<string?> FindSoleDetailItemAsync(
        int cartonNumber,
        CancellationToken cancellationToken = default)
    {
        // Two rows come back when the carton has two detail lines, which is the case the
        // ABL treats as "not found" — so the caller gets null rather than a first row.
        const string sql = """
            SELECT abs_num
            FROM   PUB.cartondtl
            WHERE  carton_num = @cartonNumber
            """;

        var items = (await _unitOfWork.Connection.QueryAsync<string?>(
            new CommandDefinition(
                sql,
                new { cartonNumber },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        if (items.Count > 1)
        {
            _logger.LogDebug(
                "Carton {CartonNumber} has {Count} detail lines; treating as ambiguous, " +
                "which is what the ABL's bare FIND does.",
                cartonNumber, items.Count);
            return null;
        }

        return items.Count == 1 ? items[0] : null;
    }

    /// <summary>One carton's detail summary, for <see cref="IsSingleUnitOrderAsync"/>.</summary>
    private sealed record CartonDetailSummary
    {
        public int CartonNumber { get; init; }

        public int DetailCount { get; init; }

        /// <summary>Null when the carton has no detail line at all — treated as zero.</summary>
        public decimal? MaxQuantity { get; init; }
    }
}
