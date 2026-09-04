using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IPickRepository"/> over <c>PUB.pick</c>.
/// </summary>
/// <remarks>
/// <para>
/// Columns verified against <c>schema/irms.df</c>. Reads go through
/// <c>co_wh_carton</c> (co_num, wh_num, carton_id), which is the index this access pattern
/// was built for.
/// </para>
/// <para>
/// ⚠ <b>Two things here have not run against a real database.</b> The extent-column syntax
/// <c>"custom_data##4"</c> is the documented OpenEdge SQL-92 spelling for element 4 of an
/// array field, and the case-insensitive status match assumes the ODBC layer honours
/// <c>IN</c> against a one-character column the obvious way. Both belong on the same DBA
/// visit as the parameter-binding question — <c>docs/SCHEMA_REVIEW.md</c> items 4 and 12.
/// </para>
/// </remarks>
public sealed class PickRepository : IPickRepository
{
    // custom_data is EXTENT 5. OpenEdge SQL-92 exposes each element as
    // <field>##<n>, which needs quoting because of the '#' characters.
    private const string SelectColumns = """
        co_num                AS Company,
        wh_num                AS Warehouse,
        id                    AS Id,
        carton_id             AS CartonId,
        pick_status           AS PickStatus,
        allow_pick            AS AllowPick,
        "order"               AS "Order",
        order_suffix          AS OrderSuffix,
        line                  AS Line,
        line_sequence         AS LineSequence,
        abs_num               AS ItemNumber,
        bin_num               AS BinNumber,
        qty                   AS Quantity,
        orig_qty              AS OriginalQuantity,
        lot                   AS Lot,
        batch                 AS Batch,
        pick_sequence         AS PickSequence,
        date_time             AS "DateTime",
        external_tote         AS ExternalTote,
        external_status       AS ExternalStatus,
        external_message      AS ExternalMessage,
        "custom_data##4"      AS ToteRobot,
        "custom_data##5"      AS ToteId
        """;

    // ⚠ CASE. pick_status has INITIAL "O" and the ABL queries it as "o"
    // (locusAPI.cls:2300). ABL comparison ignores case; SQL does not. Matching both rather
    // than UPPER(pick_status) = 'O', which cannot use the index.
    private const string OpenStatusFilter = "AND pick_status IN ('O', 'o')";

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<PickRepository> _logger;

    public PickRepository(IIrmsUnitOfWork unitOfWork, ILogger<PickRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Pick>> FindByCartonAsync(
        string company,
        string warehouse,
        string cartonId,
        bool openPicksOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(cartonId))
        {
            return [];
        }

        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.pick
            WHERE  co_num    = @company
            AND    wh_num    = @warehouse
            AND    carton_id = @cartonId
            {(openPicksOnly ? OpenStatusFilter : string.Empty)}
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<Pick>(
            new CommandDefinition(
                sql,
                new { company, warehouse, cartonId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    /// <inheritdoc />
    public async Task<int> SetToteAssignmentAsync(
        string company,
        string warehouse,
        string cartonId,
        string jobRobot,
        string toteId,
        bool openPicksOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobRobot);
        ArgumentNullException.ThrowIfNull(toteId);

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "SetToteAssignmentAsync must run inside a unit of work. The ABL takes an " +
                "EXCLUSIVE-LOCK on each pick row for this write.");
        }

        var sql = $"""
            UPDATE PUB.pick
            SET    "custom_data##4" = @jobRobot,
                   "custom_data##5" = @toteId
            WHERE  co_num    = @company
            AND    wh_num    = @warehouse
            AND    carton_id = @cartonId
            {(openPicksOnly ? OpenStatusFilter : string.Empty)}
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { jobRobot, toteId, company, warehouse, cartonId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            // The ABL loop simply does not execute, silently. Worth a line here: a carton
            // with no picks at this point usually means the tote is being inducted against
            // something already picked or cancelled.
            _logger.LogWarning(
                "No pick rows updated for {Company}/{Warehouse} carton {CartonId} " +
                "(openPicksOnly: {OpenOnly}).",
                company, warehouse, cartonId, openPicksOnly);
        }

        return affected;
    }
}
