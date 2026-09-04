using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IMovementRepository"/> over <c>PUB.movemst</c>.
/// </summary>
/// <remarks>
/// Columns verified against <c>schema/irms.df</c>, 2026-09-04. See
/// <see cref="IMovementRepository.FindReplenishmentAsync"/> for why the lookup cannot use an
/// index, and <see cref="Movement.RowIdentity"/> for the unverified <c>ROWID</c> question.
/// </remarks>
public sealed class MovementRepository : IMovementRepository
{
    // ROWID is a pseudo-column, not in the .df. It is selected because the virtual_lock
    // release keys on STRING(ROWID(bmovemst)) — locusAPI.cls:3324.
    private const string SelectColumns = """
        ROWID              AS RowIdentity,
        co_num             AS Company,
        wh_num             AS Warehouse,
        id                 AS Id,
        abs_num            AS ItemNumber,
        bin_from           AS BinFrom,
        bin_to             AS BinTo,
        zone_to            AS ZoneTo,
        to_wh_num          AS ToWarehouse,
        wh_zone            AS WarehouseZone,
        movement_type      AS MovementType,
        row_status         AS RowStatus,
        quantity           AS Quantity,
        pallet_id          AS PalletId,
        lot                AS Lot,
        lpn_scanid         AS LpnScanId,
        truck_id           AS TruckId,
        doc_id             AS DocumentId,
        task_id            AS TaskId,
        batch              AS Batch,
        dept_num           AS DepartmentNumber,
        machine            AS Machine,
        prod_id            AS ProductionId,
        prod_line          AS ProductionLine,
        urgent             AS Urgent,
        date_time          AS "DateTime",
        "custom_data##1"   AS CustomData1,
        "custom_data##2"   AS CustomData2,
        "custom_data##3"   AS CustomData3,
        "custom_data##4"   AS CustomData4,
        "custom_data##5"   AS CustomData5
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<MovementRepository> _logger;

    public MovementRepository(IIrmsUnitOfWork unitOfWork, ILogger<MovementRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Movement?> FindReplenishmentAsync(
        string company,
        string warehouse,
        string licencePlate,
        string robot,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(licencePlate)
            || string.IsNullOrWhiteSpace(robot))
        {
            // The ABL would run the FIND with blanks and simply not find anything. Same
            // outcome, without the round trip.
            return null;
        }

        if (forUpdate && !_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "A locking read must run inside a unit of work. The ABL takes " +
                "EXCLUSIVE-LOCK here (locusAPI.cls:3297).");
        }

        // The ABL's FIND FIRST has no ORDER BY; the row that comes back is whatever the
        // primary index yields first. Reproduced with an explicit ORDER BY on the primary
        // index fields so the .NET result is at least deterministic — the ABL's is not
        // guaranteed to be, and a non-deterministic pick would be untestable.
        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.movemst
            WHERE  co_num           = @company
            AND    wh_num           = @warehouse
            AND    "custom_data##1" = @licencePlate
            AND    bin_from         = @robot
            ORDER BY bin_from, abs_num, row_status
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<MovementRow>(
            new CommandDefinition(
                sql,
                new { company, warehouse, licencePlate, robot },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var matches = rows.ToList();

        if (matches.Count > 1)
        {
            // The ABL takes the first and never learns there were others. Worth knowing:
            // two open replenishments for one licence plate on one robot means the
            // completion is being applied to an arbitrary one of them.
            _logger.LogWarning(
                "{Count} movemst rows match licence plate {Plate} on robot {Robot} in " +
                "{Company}/{Warehouse}. Taking the first, as the ABL does.",
                matches.Count, licencePlate, robot, company, warehouse);
        }

        return matches.Count == 0 ? null : matches[0].ToDomain();
    }

    /// <inheritdoc />
    public async Task<Movement?> FindOpenByToteAsync(
        string company,
        string warehouse,
        string tote,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(tote))
        {
            return null;
        }

        // row_status is a one-character column the ABL compares case-insensitively. Both
        // cases of both statuses are listed rather than UPPER(), which would cost the
        // primary index co_wh_from_abs_status — and bin_from + row_status are two of its
        // five fields, so this lookup does use it.
        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.movemst
            WHERE  co_num     = @company
            AND    wh_num     = @warehouse
            AND    bin_from   = @tote
            AND    row_status IN ('A', 'a', 'O', 'o')
            ORDER BY abs_num, row_status
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<MovementRow>(
            new CommandDefinition(
                sql,
                new { company, warehouse, tote },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.FirstOrDefault()?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<Movement?> FindByIdAsync(
        string company,
        string warehouse,
        int id,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        if (forUpdate && !_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "A locking read must run inside a unit of work. The ABL takes " +
                "EXCLUSIVE-LOCK here (locusAPI.cls:4147).");
        }

        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.movemst
            WHERE  id     = @id
            AND    co_num = @company
            AND    wh_num = @warehouse
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;

        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<MovementRow>(
            new CommandDefinition(
                sql,
                new { id, company, warehouse },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<decimal> AdjustQuantityAsync(
        int id,
        decimal delta,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Adjusting a movement quantity must run inside a unit of work — the row is " +
                "deleted in the same transaction when the result reaches zero.");
        }

        await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE PUB.movemst SET quantity = quantity + @delta WHERE id = @id",
                new { delta, id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return await _unitOfWork.Connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                "SELECT quantity FROM PUB.movemst WHERE id = @id",
                new { id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        string company,
        string warehouse,
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Deleting a movement must run inside a unit of work. It commits together " +
                "with the inventory change and the MR transaction that record why it went.");
        }

        // co_num and wh_num are redundant against a UNIQUE id, and included anyway: a
        // delete that can only ever affect one warehouse cannot go wrong across tenants
        // if the id is ever wrong.
        return await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM PUB.movemst
                WHERE  id     = @id
                AND    co_num = @company
                AND    wh_num = @warehouse
                """,
                new { id, company, warehouse },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Flat shape Dapper can materialise; <c>custom_data</c> is five columns.</summary>
    private sealed record MovementRow
    {
        public string? RowIdentity { get; init; }
        public string Company { get; init; } = string.Empty;
        public string Warehouse { get; init; } = string.Empty;
        public int Id { get; init; }
        public string? ItemNumber { get; init; }
        public string? BinFrom { get; init; }
        public string? BinTo { get; init; }
        public string? ZoneTo { get; init; }
        public string? ToWarehouse { get; init; }
        public string? WarehouseZone { get; init; }
        public string? MovementType { get; init; }
        public string? RowStatus { get; init; }
        public decimal Quantity { get; init; }
        public string? PalletId { get; init; }
        public string? Lot { get; init; }
        public string? LpnScanId { get; init; }
        public string? TruckId { get; init; }
        public string? DocumentId { get; init; }
        public int? TaskId { get; init; }
        public int Batch { get; init; }
        public int DepartmentNumber { get; init; }
        public string? Machine { get; init; }
        public int? ProductionId { get; init; }
        public int? ProductionLine { get; init; }
        public bool Urgent { get; init; }
        public string? DateTime { get; init; }
        public string? CustomData1 { get; init; }
        public string? CustomData2 { get; init; }
        public string? CustomData3 { get; init; }
        public string? CustomData4 { get; init; }
        public string? CustomData5 { get; init; }

        public Movement ToDomain() => new()
        {
            RowIdentity = RowIdentity,
            Company = Company,
            Warehouse = Warehouse,
            Id = Id,
            ItemNumber = ItemNumber,
            BinFrom = BinFrom,
            BinTo = BinTo,
            ZoneTo = ZoneTo,
            ToWarehouse = ToWarehouse,
            WarehouseZone = WarehouseZone,
            MovementType = MovementType,
            RowStatus = RowStatus,
            Quantity = Quantity,
            PalletId = PalletId,
            Lot = Lot,
            LpnScanId = LpnScanId,
            TruckId = TruckId,
            DocumentId = DocumentId,
            TaskId = TaskId,
            Batch = Batch,
            DepartmentNumber = DepartmentNumber,
            Machine = Machine,
            ProductionId = ProductionId,
            ProductionLine = ProductionLine,
            Urgent = Urgent,
            DateTime = DateTime,
            CustomData = [CustomData1, CustomData2, CustomData3, CustomData4, CustomData5],
        };
    }
}
