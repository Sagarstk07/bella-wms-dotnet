using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IInventoryRepository"/> over <c>PUB.inventory</c>.
/// </summary>
/// <remarks>
/// <para>
/// Columns verified against <c>schema/irms.df</c>, 2026-09-04. Reads go through
/// <c>co_wh_bin_pallet_abs</c>, which is UNIQUE and matches the predicate exactly.
/// </para>
/// <para>
/// ⚠ Three constructs here have never run against a real database: the extent spelling
/// <c>"custom_data##1"</c>, <c>FOR UPDATE</c> through the OpenEdge ODBC driver, and Dapper's
/// named parameters against a driver that may bind positionally.
/// <c>docs/SCHEMA_REVIEW.md</c> items 4, 5 and 15.
/// </para>
/// </remarks>
public sealed class InventoryRepository : IInventoryRepository
{
    private const string SelectColumns = """
        co_num             AS Company,
        wh_num             AS Warehouse,
        id                 AS Id,
        bin_num            AS BinNumber,
        pallet_id          AS PalletId,
        abs_num            AS ItemNumber,
        total_qty          AS TotalQuantity,
        reserved_qty       AS ReservedQuantity,
        stock_stat         AS StockStatus,
        vendor_id          AS VendorId,
        lot                AS Lot,
        uom                AS UnitOfMeasure,
        case_qty           AS CaseQuantity,
        expiration         AS Expiration,
        date_time          AS "DateTime",
        emp_num            AS EmployeeNumber,
        ns_comment         AS NsComment,
        attributes         AS Attributes,
        suggested_bin      AS SuggestedBin,
        truck_id           AS TruckId,
        po_number          AS PoNumber,
        po_suffix          AS PoSuffix,
        "order"            AS "Order",
        order_suffix       AS OrderSuffix,
        item_cost          AS ItemCost,
        country_code       AS CountryCode,
        cargo_control      AS CargoControl,
        task_id            AS TaskId,
        qa_release_id      AS QaReleaseId,
        cross_docking      AS CrossDocking,
        cycle_flag         AS CycleFlag,
        cycle_id           AS CycleId,
        cycle_cnum         AS CycleCountNumber,
        cycle_level        AS CycleLevel,
        cycle_emp_num      AS CycleEmployeeNumber,
        rtn_category       AS ReturnCategory,
        rtn_pallet_full    AS ReturnPalletFull,
        "custom_data##1"   AS CustomData1,
        "custom_data##2"   AS CustomData2,
        "custom_data##3"   AS CustomData3,
        "custom_data##4"   AS CustomData4,
        "custom_data##5"   AS CustomData5
        """;

    // Every column, because CreateAsync copies 30 fields off another row and the four
    // INITIAL-only columns have to be written or they land unset. cross_docking,
    // cycle_cnum, qa_release_id and reserved_qty are the four ABL CREATE would have
    // defaulted; they are in the list with the model's own defaults behind them.
    private const string InsertSql = """
        INSERT INTO PUB.inventory
            (id, co_num, wh_num, bin_num, pallet_id, abs_num, total_qty, reserved_qty,
             stock_stat, vendor_id, lot, uom, case_qty, expiration, date_time, emp_num,
             ns_comment, attributes, suggested_bin, truck_id, po_number, po_suffix,
             "order", order_suffix, item_cost, country_code, cargo_control, task_id,
             qa_release_id, cross_docking, cycle_flag, cycle_id, cycle_cnum, cycle_level,
             cycle_emp_num, rtn_category, rtn_pallet_full,
             "custom_data##1", "custom_data##2", "custom_data##3", "custom_data##4",
             "custom_data##5")
        VALUES
            (@Id, @Company, @Warehouse, @BinNumber, @PalletId, @ItemNumber, @TotalQuantity,
             @ReservedQuantity, @StockStatus, @VendorId, @Lot, @UnitOfMeasure, @CaseQuantity,
             @Expiration, @DateTime, @EmployeeNumber, @NsComment, @Attributes, @SuggestedBin,
             @TruckId, @PoNumber, @PoSuffix, @Order, @OrderSuffix, @ItemCost, @CountryCode,
             @CargoControl, @TaskId, @QaReleaseId, @CrossDocking, @CycleFlag, @CycleId,
             @CycleCountNumber, @CycleLevel, @CycleEmployeeNumber, @ReturnCategory,
             @ReturnPalletFull, @CustomData1, @CustomData2, @CustomData3, @CustomData4,
             @CustomData5)
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ISequenceAllocator _sequences;
    private readonly ILogger<InventoryRepository> _logger;

    public InventoryRepository(
        IIrmsUnitOfWork unitOfWork,
        ISequenceAllocator sequences,
        ILogger<InventoryRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Inventory?> FindByLocationAsync(
        string company,
        string warehouse,
        string binNumber,
        string palletId,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(palletId);

        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(binNumber)
            || string.IsNullOrWhiteSpace(itemNumber))
        {
            return null;
        }

        if (forUpdate && !_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "A locking read must run inside a unit of work. The ABL takes " +
                "EXCLUSIVE-LOCK here (locusAPI.cls:3355).");
        }

        // bin_num is stored uppercase by w_invent.t, so the parameter is upper-cased to
        // match rather than the column being wrapped in UPPER() — which would cost the
        // index. The pallet and item comparisons stay literal: those columns have no
        // case-normalising trigger, and ABL's own comparison ignores case in a way SQL
        // cannot reproduce without giving up the index. Flagged, not solved.
        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.inventory
            WHERE  co_num    = @company
            AND    wh_num    = @warehouse
            AND    bin_num   = @binNumber
            AND    pallet_id = @palletId
            AND    abs_num   = @itemNumber
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;

        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<InventoryRow>(
            new CommandDefinition(
                sql,
                new
                {
                    company,
                    warehouse,
                    binNumber = binNumber.ToUpperInvariant(),
                    palletId,
                    itemNumber,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Inventory>> FindByBinAsync(
        string company,
        string warehouse,
        string binNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(binNumber))
        {
            return [];
        }

        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.inventory
            WHERE  co_num  = @company
            AND    wh_num  = @warehouse
            AND    bin_num = @binNumber
            ORDER BY abs_num, pallet_id
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<InventoryRow>(
            new CommandDefinition(
                sql,
                new { company, warehouse, binNumber = binNumber.ToUpperInvariant() },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // The ABL's FOR EACH has no ORDER BY and takes the primary index's order. Ordered
        // explicitly here so the sequence of writes is reproducible between runs — with no
        // ordering, a failure part-way through a multi-item tote would be untestable.
        return [.. rows.Select(r => r.ToDomain())];
    }

    /// <inheritdoc />
    public async Task<Inventory?> FindByBinAndItemAsync(
        string company,
        string warehouse,
        string binNumber,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(company)
            || string.IsNullOrWhiteSpace(warehouse)
            || string.IsNullOrWhiteSpace(binNumber)
            || string.IsNullOrWhiteSpace(itemNumber))
        {
            return null;
        }

        if (forUpdate && !_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "A locking read must run inside a unit of work. The ABL takes " +
                "EXCLUSIVE-LOCK here (locusAPI.cls:3667).");
        }

        // No pallet_id filter — see the interface remarks. That drops the query off the
        // unique index onto co_wh_bin_pallet_abs's leading columns, so more than one row can
        // match. ORDER BY makes which one comes back deterministic; the ABL's FIND FIRST is
        // not.
        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.inventory
            WHERE  co_num  = @company
            AND    wh_num  = @warehouse
            AND    bin_num = @binNumber
            AND    abs_num = @itemNumber
            ORDER BY pallet_id
            {(forUpdate ? "FOR UPDATE" : string.Empty)}
            """;

        var rows = await _unitOfWork.Connection.QueryAsync<InventoryRow>(
            new CommandDefinition(
                sql,
                new
                {
                    company,
                    warehouse,
                    binNumber = binNumber.ToUpperInvariant(),
                    itemNumber,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var matches = rows.ToList();

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "{Count} inventory rows hold item {Item} in bin {Bin} ({Company}/{Warehouse}) " +
                "on different pallets. Taking the first, as the ABL does — but the caller's " +
                "loop will visit all of them and adjust this one repeatedly.",
                matches.Count, itemNumber, binNumber, company, warehouse);
        }

        return matches.Count == 0 ? null : matches[0].ToDomain();
    }

    /// <inheritdoc />
    public async Task<int> SetDateTimeAsync(
        int id,
        string? dateTime,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Setting date_time must run inside a unit of work.");
        }

        // The datetim.t field trigger rejects anything that is not 12 characters of
        // "YYYYMMDDHHMM", empty, or unknown. The database enforces it, so a malformed value
        // fails the write rather than being stored — no client-side check is added here,
        // deliberately: duplicating the rule invites the two copies to disagree.
        return await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE PUB.inventory SET date_time = @dateTime WHERE id = @id",
                new { dateTime, id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
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
                "Adjusting stock must run inside a unit of work. A quantity change that " +
                "commits independently of the transaction and audit row that explain it " +
                "leaves the warehouse unable to reconcile.");
        }

        await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE PUB.inventory SET total_qty = total_qty + @delta WHERE id = @id",
                new { delta, id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var total = await _unitOfWork.Connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(
                "SELECT total_qty FROM PUB.inventory WHERE id = @id",
                new { id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (total < 0)
        {
            // Not an error here — the caller decides. But it is always worth a log line:
            // negative stock means the record and the shelf disagree.
            _logger.LogWarning(
                "inventory {Id} went negative ({Total}) after an adjustment of {Delta}. " +
                "The caller should be flagging a cycle count.",
                id, total, delta);
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(
        Inventory inventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Creating an inventory record must run inside a unit of work — the id is " +
                "allocated from a cycling sequence and checked for collisions in the same " +
                "transaction that inserts the row.");
        }

        // Converts n_invent.t: draw from inventory_id, retry past values still in use.
        var id = await _sequences
            .NextAsync("inventory_id", "inventory", "id", cancellationToken)
            .ConfigureAwait(false);

        var custom = inventory.CustomData;

        var parameters = new
        {
            Id = id,
            inventory.Company,
            inventory.Warehouse,
            // Converts the one live line of w_invent.t (line 77 onwards is compiled out).
            BinNumber = inventory.BinNumber?.ToUpperInvariant(),
            inventory.PalletId,
            inventory.ItemNumber,
            inventory.TotalQuantity,
            inventory.ReservedQuantity,
            inventory.StockStatus,
            inventory.VendorId,
            inventory.Lot,
            inventory.UnitOfMeasure,
            inventory.CaseQuantity,
            inventory.Expiration,
            inventory.DateTime,
            inventory.EmployeeNumber,
            inventory.NsComment,
            inventory.Attributes,
            inventory.SuggestedBin,
            inventory.TruckId,
            inventory.PoNumber,
            inventory.PoSuffix,
            inventory.Order,
            inventory.OrderSuffix,
            inventory.ItemCost,
            inventory.CountryCode,
            inventory.CargoControl,
            inventory.TaskId,
            inventory.QaReleaseId,
            inventory.CrossDocking,
            inventory.CycleFlag,
            inventory.CycleId,
            inventory.CycleCountNumber,
            inventory.CycleLevel,
            inventory.CycleEmployeeNumber,
            inventory.ReturnCategory,
            inventory.ReturnPalletFull,
            CustomData1 = Slot(custom, 0),
            CustomData2 = Slot(custom, 1),
            CustomData3 = Slot(custom, 2),
            CustomData4 = Slot(custom, 3),
            CustomData5 = Slot(custom, 4),
        };

        await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                InsertSql,
                parameters,
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        _logger.LogDebug(
            "Created inventory {Id} at {Warehouse}/{Bin} for item {Item}.",
            id, inventory.Warehouse, inventory.BinNumber, inventory.ItemNumber);

        return id;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Deleting an inventory record must run inside a unit of work.");
        }

        // d_invent.t is a complete no-op — line 51 opens `&If 0 &Then` with no `&Endif`,
        // so the preprocessor discards the rest of the file. Nothing to convert.
        return await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM PUB.inventory WHERE id = @id",
                new { id },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string? Slot(string?[]? data, int index) =>
        data is not null && index < data.Length ? data[index] : null;

    /// <summary>
    /// Flat shape Dapper can materialise. <c>custom_data</c> is five columns over SQL-92,
    /// and Dapper will not fold five columns into one array property.
    /// </summary>
    private sealed record InventoryRow
    {
        public string Company { get; init; } = string.Empty;
        public string Warehouse { get; init; } = string.Empty;
        public int Id { get; init; }
        public string? BinNumber { get; init; }
        public string? PalletId { get; init; }
        public string? ItemNumber { get; init; }
        public decimal TotalQuantity { get; init; }
        public decimal ReservedQuantity { get; init; }
        public string? StockStatus { get; init; }
        public string? VendorId { get; init; }
        public string? Lot { get; init; }
        public string? UnitOfMeasure { get; init; }
        public int CaseQuantity { get; init; }
        public DateOnly? Expiration { get; init; }
        public string? DateTime { get; init; }
        public string? EmployeeNumber { get; init; }
        public string? NsComment { get; init; }
        public string? Attributes { get; init; }
        public string? SuggestedBin { get; init; }
        public string? TruckId { get; init; }
        public string? PoNumber { get; init; }
        public string? PoSuffix { get; init; }
        public string? Order { get; init; }
        public string? OrderSuffix { get; init; }
        public decimal ItemCost { get; init; }
        public string? CountryCode { get; init; }
        public string? CargoControl { get; init; }
        public int? TaskId { get; init; }
        public int QaReleaseId { get; init; }
        public bool CrossDocking { get; init; }
        public bool CycleFlag { get; init; }
        public int? CycleId { get; init; }
        public int CycleCountNumber { get; init; }
        public string? CycleLevel { get; init; }
        public string? CycleEmployeeNumber { get; init; }
        public string? ReturnCategory { get; init; }
        public bool ReturnPalletFull { get; init; }
        public string? CustomData1 { get; init; }
        public string? CustomData2 { get; init; }
        public string? CustomData3 { get; init; }
        public string? CustomData4 { get; init; }
        public string? CustomData5 { get; init; }

        public Inventory ToDomain() => new()
        {
            Company = Company,
            Warehouse = Warehouse,
            Id = Id,
            BinNumber = BinNumber,
            PalletId = PalletId,
            ItemNumber = ItemNumber,
            TotalQuantity = TotalQuantity,
            ReservedQuantity = ReservedQuantity,
            StockStatus = StockStatus,
            VendorId = VendorId,
            Lot = Lot,
            UnitOfMeasure = UnitOfMeasure,
            CaseQuantity = CaseQuantity,
            Expiration = Expiration,
            DateTime = DateTime,
            EmployeeNumber = EmployeeNumber,
            NsComment = NsComment,
            Attributes = Attributes,
            SuggestedBin = SuggestedBin,
            TruckId = TruckId,
            PoNumber = PoNumber,
            PoSuffix = PoSuffix,
            Order = Order,
            OrderSuffix = OrderSuffix,
            ItemCost = ItemCost,
            CountryCode = CountryCode,
            CargoControl = CargoControl,
            TaskId = TaskId,
            QaReleaseId = QaReleaseId,
            CrossDocking = CrossDocking,
            CycleFlag = CycleFlag,
            CycleId = CycleId,
            CycleCountNumber = CycleCountNumber,
            CycleLevel = CycleLevel,
            CycleEmployeeNumber = CycleEmployeeNumber,
            ReturnCategory = ReturnCategory,
            ReturnPalletFull = ReturnPalletFull,
            CustomData = [CustomData1, CustomData2, CustomData3, CustomData4, CustomData5],
        };
    }
}
