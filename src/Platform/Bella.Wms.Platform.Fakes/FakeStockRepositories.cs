using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Platform.Fakes;

/// <summary>
/// In-memory <c>inventory</c>, <c>movemst</c> and <c>virtual_lock</c> for the no-database
/// host.
/// </summary>
/// <remarks>
/// <para>
/// Kept separate from <see cref="InMemoryWarehouse"/> for now because nothing yet needs
/// stock and cartons to share state — the putaway handlers that will are not written. Fold
/// it in at that point rather than leaving two stores that can disagree about the same
/// warehouse.
/// </para>
/// <para>
/// The seed is one pallet of an item on a robot with a movement pointing at a shelf, which
/// is the shape <c>replenputcomplete</c> expects to find.
/// </para>
/// </remarks>
public sealed class InMemoryStock
{
    private int _nextInventoryId = 500;

    public List<Inventory> Inventory { get; } = [];

    public List<Movement> Movements { get; } = [];

    /// <summary>Keyed by <c>(tablename, record_id)</c>, lower-cased table name.</summary>
    public Dictionary<(string Table, string RecordId), VirtualLock> Locks { get; } = [];

    /// <summary>Record ids the fake should report as physically held by another session.</summary>
    public HashSet<(string Table, string RecordId)> HeldLocks { get; } = [];

    public List<Item> Items { get; } = [];

    /// <summary>Movement ids with an open <c>FM</c> transaction against them.</summary>
    public HashSet<int> OpenFulfillments { get; } = [];

    public int NextInventoryId() => _nextInventoryId++;

    public static InMemoryStock CreateSeeded()
    {
        var stock = new InMemoryStock();

        stock.Inventory.Add(new Inventory
        {
            Company = "001",
            Warehouse = "01",
            Id = 401,
            BinNumber = "ROBOT01",
            PalletId = "PLT0000000000001",
            ItemNumber = "WIDGET-1",
            TotalQuantity = 24m,
            StockStatus = "A",
            VendorId = "V001",
            CaseQuantity = 12,
            UnitOfMeasure = "EA",
            DateTime = "202609040800",
        });

        stock.Movements.Add(new Movement
        {
            Company = "001",
            Warehouse = "01",
            Id = 9001,
            ItemNumber = "WIDGET-1",
            BinFrom = "ROBOT01",
            BinTo = "A-01-01",
            RowStatus = "O",
            MovementType = "RP",
            Quantity = 24m,
            PalletId = "PLT0000000000001",
            WarehouseZone = "P",
            ToWarehouse = "01",
            DateTime = "202609040800",
            // Slot 1 is the SSCC-18 the Locus payload arrives with; slot 4 is the build time.
            CustomData = ["00000000000000000001", null, null, "202609040745", null],
            // A stand-in for STRING(ROWID(movemst)). The real one comes from SQL's ROWID
            // pseudo-column and its form is unverified — see Movement.RowIdentity.
            RowIdentity = "0x000023f1",
        });

        stock.OpenFulfillments.Add(9001);

        stock.Items.Add(new Item
        {
            ItemNumber = "WIDGET-1",
            ItemType = "S",
            CaseQuantity = 12,
        });

        return stock;
    }
}

/// <summary>In-memory <see cref="IItemRepository"/>.</summary>
public sealed class FakeItemRepository : IItemRepository
{
    private readonly InMemoryStock _stock;

    public FakeItemRepository(InMemoryStock stock) =>
        _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    public Task<Item?> FindAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemNumber))
        {
            return Task.FromResult<Item?>(null);
        }

        return Task.FromResult(_stock.Items.FirstOrDefault(i =>
            string.Equals(i.ItemNumber, itemNumber, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<int> GetCaseQuantityAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(company, warehouse, itemNumber, cancellationToken)
            .ConfigureAwait(false);

        // 1, not 0 — static_connect.cls:2724.
        return item?.CaseQuantity ?? 1;
    }
}

/// <summary>In-memory <see cref="IFulfillmentRepository"/>.</summary>
public sealed class FakeFulfillmentRepository : IFulfillmentRepository
{
    private readonly InMemoryStock _stock;

    public FakeFulfillmentRepository(InMemoryStock stock) =>
        _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    public Task<int> CloseForMovementAsync(
        string company,
        string warehouse,
        int movementId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_stock.OpenFulfillments.Remove(movementId) ? 1 : 0);
}

/// <summary>In-memory <see cref="IInventoryRepository"/>.</summary>
public sealed class FakeInventoryRepository : IInventoryRepository
{
    private readonly InMemoryStock _stock;

    public FakeInventoryRepository(InMemoryStock stock) =>
        _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    public Task<Inventory?> FindByLocationAsync(
        string company,
        string warehouse,
        string binNumber,
        string palletId,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        // Case-insensitive throughout, which is what ABL does and what the real repository
        // has to work to reproduce.
        var match = _stock.Inventory.FirstOrDefault(i =>
            string.Equals(i.Company, company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.BinNumber, binNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.PalletId ?? string.Empty, palletId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.ItemNumber, itemNumber, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Inventory>> FindByBinAsync(
        string company,
        string warehouse,
        string binNumber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Inventory> matches =
        [
            .. _stock.Inventory
                .Where(i =>
                    string.Equals(i.Company, company, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.BinNumber, binNumber, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.ItemNumber, StringComparer.Ordinal)
                .ThenBy(i => i.PalletId, StringComparer.Ordinal),
        ];

        return Task.FromResult(matches);
    }

    public Task<Inventory?> FindByBinAndItemAsync(
        string company,
        string warehouse,
        string binNumber,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        // No pallet filter — deliberately, matching the real repository.
        var match = _stock.Inventory
            .Where(i =>
                string.Equals(i.Company, company, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.BinNumber, binNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.ItemNumber, itemNumber, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.PalletId, StringComparer.Ordinal)
            .FirstOrDefault();

        return Task.FromResult(match);
    }

    public Task<int> SetDateTimeAsync(
        int id,
        string? dateTime,
        CancellationToken cancellationToken = default)
    {
        var index = _stock.Inventory.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            return Task.FromResult(0);
        }

        _stock.Inventory[index] = _stock.Inventory[index] with { DateTime = dateTime };
        return Task.FromResult(1);
    }

    public Task<decimal> AdjustQuantityAsync(
        int id,
        decimal delta,
        CancellationToken cancellationToken = default)
    {
        var index = _stock.Inventory.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            return Task.FromResult(0m);
        }

        var updated = _stock.Inventory[index] with
        {
            TotalQuantity = _stock.Inventory[index].TotalQuantity + delta,
        };

        _stock.Inventory[index] = updated;

        return Task.FromResult(updated.TotalQuantity);
    }

    public Task<int> CreateAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var id = _stock.NextInventoryId();

        // Reproduces the one live line of w_invent.t, so a test that checks bin casing
        // gets the same answer from the fake as from the database.
        _stock.Inventory.Add(inventory with
        {
            Id = id,
            BinNumber = inventory.BinNumber?.ToUpperInvariant(),
        });

        return Task.FromResult(id);
    }

    public Task<int> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_stock.Inventory.RemoveAll(i => i.Id == id));
}

/// <summary>In-memory <see cref="IMovementRepository"/>.</summary>
public sealed class FakeMovementRepository : IMovementRepository
{
    private readonly InMemoryStock _stock;

    public FakeMovementRepository(InMemoryStock stock) =>
        _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    public Task<Movement?> FindReplenishmentAsync(
        string company,
        string warehouse,
        string licencePlate,
        string robot,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var match = _stock.Movements.FirstOrDefault(m =>
            string.Equals(m.Company, company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.CustomData.ElementAtOrDefault(0), licencePlate, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.BinFrom, robot, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<Movement?> FindOpenByToteAsync(
        string company,
        string warehouse,
        string tote,
        CancellationToken cancellationToken = default)
    {
        var match = _stock.Movements.FirstOrDefault(m =>
            string.Equals(m.Company, company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.BinFrom, tote, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(m.RowStatus, "A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.RowStatus, "O", StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult(match);
    }

    public Task<Movement?> FindByIdAsync(
        string company,
        string warehouse,
        int id,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var match = _stock.Movements.FirstOrDefault(m =>
            m.Id == id
            && string.Equals(m.Company, company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<decimal> AdjustQuantityAsync(
        int id,
        decimal delta,
        CancellationToken cancellationToken = default)
    {
        var index = _stock.Movements.FindIndex(m => m.Id == id);
        if (index < 0)
        {
            return Task.FromResult(0m);
        }

        var updated = _stock.Movements[index] with
        {
            Quantity = _stock.Movements[index].Quantity + delta,
        };

        _stock.Movements[index] = updated;
        return Task.FromResult(updated.Quantity);
    }

    public Task<int> DeleteAsync(
        string company,
        string warehouse,
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_stock.Movements.RemoveAll(m =>
            m.Id == id
            && string.Equals(m.Company, company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Warehouse, warehouse, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>In-memory <see cref="IVirtualLockRepository"/>.</summary>
/// <remarks>
/// The fake can express the "held by another session" case cleanly, through
/// <see cref="InMemoryStock.HeldLocks"/>, which is the point: the real implementation
/// reaches that state only through a caught database exception, so the fake is where that
/// branch can actually be tested.
/// </remarks>
public sealed class FakeVirtualLockRepository : IVirtualLockRepository
{
    private readonly InMemoryStock _stock;

    public FakeVirtualLockRepository(InMemoryStock stock) =>
        _stock = stock ?? throw new ArgumentNullException(nameof(stock));

    public Task<VirtualLockState> GetStateAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        var key = (tableName.ToLowerInvariant(), recordId);

        if (_stock.HeldLocks.Contains(key))
        {
            return Task.FromResult(VirtualLockState.HeldByAnotherSession);
        }

        return Task.FromResult(_stock.Locks.ContainsKey(key)
            ? VirtualLockState.Releasable
            : VirtualLockState.NotLocked);
    }

    public Task<int> ReleaseAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_stock.Locks.Remove((tableName.ToLowerInvariant(), recordId)) ? 1 : 0);
}
