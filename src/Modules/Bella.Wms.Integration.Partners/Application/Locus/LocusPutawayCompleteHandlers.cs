using System.Globalization;
using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// REPLENPUTCOMPLETE — a Locus robot reports that it has put stock away. Converts
/// <c>locusAPI.cls:3242-3587</c> (345 lines).
/// </summary>
/// <remarks>
/// <para>
/// The largest handler in the module and the first to move physical stock. In one
/// transaction it closes the fulfillment, releases the record lock, deletes the movement,
/// takes the quantity off the robot, puts it onto the shelf, and writes up to two
/// transaction rows. Any partial application of that leaves the warehouse's records
/// disagreeing with its shelves.
/// </para>
/// <para>
/// <b>Four ABL defects are reproduced here, not fixed.</b> Each is marked <c>⚠ ABL DEFECT</c>
/// at the line that carries it. They are, in descending order of consequence:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A new shelf row gets no FIFO timestamp</b> (<c>locusAPI.cls:3427</c>). The condition
/// guarding the copy can never be true. <c>date_time</c> is the fourth field of
/// <c>co_wh_abs_fifo</c>, the primary index, so a blank sorts ahead of every dated row and
/// replenished stock is always consumed first.
/// </description></item>
/// <item><description>
/// <b>The destination cycle-count check runs before the quantity is added</b>
/// (<c>3439</c> vs <c>3456</c>), so it can only ever catch a pre-existing negative. The
/// source-side check at <c>3360</c> is correctly ordered.
/// </description></item>
/// <item><description>
/// <b>The stock-adjustment row's case quantity reads an unpopulated buffer</b>
/// (<c>3495</c>) and therefore always resolves to 1.
/// </description></item>
/// <item><description>
/// <b>The "movement not found" exit does not undo</b> (<c>3577</c>) where every other error
/// path does. Harmless today because nothing has been written by that point.
/// </description></item>
/// </list>
/// <para>
/// <b>One thing that is not a defect, and was nearly recorded as one:</b> the two audit rows
/// take their employee number from different variables — <c>cExecUser</c> and
/// <c>static_connect:gsuserid</c>. Those are the same value; <c>cExecUser</c> is assigned
/// from <c>gsuserid</c> at <c>locusAPI.cls:236</c>.
/// </para>
/// </remarks>
public sealed class LocusReplenPutCompleteHandler : LocusEventHandlerBase
{
    /// <summary><c>{&amp;T_SADJ}</c> — stock adjustment. <c>globals/globals.i:163</c>.</summary>
    private const string StockAdjustment = "AS";

    /// <summary><c>{&amp;T_REPL}</c> — replenishment. <c>globals/globals.i:192</c>.</summary>
    private const string Replenishment = "MR";

    /// <summary>Business rule for a stock status appearing where there was none. <c>locusAPI.cls:3467</c>.</summary>
    private const int RuleStatusAdded = 7007;

    /// <summary>Business rule for a stock status changing. <c>locusAPI.cls:3471</c>.</summary>
    private const int RuleStatusChanged = 7004;

    private readonly IMovementRepository _movements;
    private readonly IInventoryRepository _inventory;
    private readonly IVirtualLockRepository _locks;
    private readonly IFulfillmentRepository _fulfillments;
    private readonly IItemRepository _items;
    private readonly ICycleCountService _cycleCounts;
    private readonly IAuditService _audit;
    private readonly IBusinessRuleService _rules;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    public LocusReplenPutCompleteHandler(
        IMovementRepository movements,
        IInventoryRepository inventory,
        IVirtualLockRepository locks,
        IFulfillmentRepository fulfillments,
        IItemRepository items,
        ICycleCountService cycleCounts,
        IAuditService audit,
        IBusinessRuleService rules,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusReplenPutCompleteHandler> logger)
        : base(logger)
    {
        _movements = movements ?? throw new ArgumentNullException(nameof(movements));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _fulfillments = fulfillments ?? throw new ArgumentNullException(nameof(fulfillments));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _cycleCounts = cycleCounts ?? throw new ArgumentNullException(nameof(cycleCounts));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // The ABL's statement order inside REPLEN-BLOCK: movemst (3292), transactions
        // (3308), virtual_lock (3322), inventory (3348), item (3473). Declared rather than
        // left emergent because both stacks run against one database for the whole parallel
        // run — a handler that took inventory before movemst would deadlock against this
        // one under load, and the failure would look like a Locus timeout.
        _lockOrder = LockOrder.Declare(
            "locus replenputcomplete",
            "api/wms/locusAPI.cls:3242-3587",
            "movemst",
            "transactions",
            "virtual_lock",
            "inventory",
            "item");
    }

    public override string EventType => LocusEventType.ReplenPutComplete;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        var licencePlate = ReadString(root, "LicensePlate") ?? string.Empty;
        var robot = ReadString(root, "JobRobot") ?? string.Empty;

        // JobStatus is parsed at locusAPI.cls:3269 and never read. Not parsed here.
        var movedQuantity = ReadExecutedQuantity(root);

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await CompleteAsync(
                licencePlate, robot, movedQuantity, cancellationToken).ConfigureAwait(false);

            if (result.HandlerSucceeded)
            {
                await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Converts `undo REPLEN-BLOCK, return`. Every failure path in the ABL
                // undoes the whole block; a half-applied stock move is worse than none.
                await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<InboundEventResult> CompleteAsync(
        string licencePlate,
        string robot,
        int movedQuantity,
        CancellationToken cancellationToken)
    {
        var company = _context.Company;
        var warehouse = _context.Warehouse;

        var movement = await _movements.FindReplenishmentAsync(
            company, warehouse, licencePlate, robot, forUpdate: true, cancellationToken)
            .ConfigureAwait(false);

        if (movement is null)
        {
            // Verified: locusAPI.cls:3578.
            //
            // ⚠ ABL DEFECT 4. The ABL's exit here is a bare `return` inside `do
            // transaction:` — it commits, where every other failure path undoes. Nothing
            // has been written yet, so committing and rolling back are the same thing; the
            // caller rolls back, which is the safer of two equivalent choices.
            return Fail($"Unable to find movemst with carton {licencePlate} and robot {robot}");
        }

        // movemst.custom_data[4]. Carried through to the MR row's custom_data[4] at the
        // very end (locusAPI.cls:3305, 3553).
        var replenishmentBuiltTime = movement.CustomData.ElementAtOrDefault(3);

        await _fulfillments
            .CloseForMovementAsync(company, warehouse, movement.Id, cancellationToken)
            .ConfigureAwait(false);

        var lockState = await _locks
            .GetStateAsync("movemst", movement.RowIdentity ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (lockState == VirtualLockState.HeldByAnotherSession)
        {
            // Verified: locusAPI.cls:3331. The missing space before "record" is in the ABL
            // and is on the wire, so it stays.
            return Fail(
                $"movemst with carton {licencePlate} and robot {robot}record is locked by a user");
        }

        if (lockState == VirtualLockState.Releasable)
        {
            await _locks
                .ReleaseAsync("movemst", movement.RowIdentity ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await _movements
            .DeleteAsync(company, warehouse, movement.Id, cancellationToken)
            .ConfigureAwait(false);

        // --- Source: take the stock off the robot. -------------------------------------

        var source = await _inventory.FindByLocationAsync(
            company,
            warehouse,
            movement.BinFrom ?? string.Empty,
            movement.PalletId ?? string.Empty,
            movement.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            // Verified: locusAPI.cls:3366.
            return Fail($"Inventory record on robot {movement.BinFrom} is not available");
        }

        var sourceTotal = await _inventory
            .AdjustQuantityAsync(source.Id, -movedQuantity, cancellationToken)
            .ConfigureAwait(false);

        if (sourceTotal < 0)
        {
            // Correctly ordered — after the subtraction. Compare with the destination
            // check below, which is not.
            var flagged = await _cycleCounts.FlagForCountAsync(
                source.Company, source.Warehouse, _context.UserId, source.Id, cancellationToken)
                .ConfigureAwait(false);

            if (flagged.Failed)
            {
                // Verified: locusAPI.cls:3372.
                return Fail("Error setting cycle count.");
            }
        }

        // --- Destination: put it onto the shelf. ---------------------------------------

        // pallet_id is matched as the empty string, not ignored: a primary pick location
        // holds stock split off a pallet and carries no pallet id (locusAPI.cls:3385).
        var destination = await _inventory.FindByLocationAsync(
            company,
            warehouse,
            movement.BinTo ?? string.Empty,
            palletId: string.Empty,
            movement.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (destination is null)
        {
            destination = BuildShelfRow(source, movement);

            var id = await _inventory.CreateAsync(destination, cancellationToken)
                .ConfigureAwait(false);

            destination = destination with { Id = id };
        }

        // ⚠ ABL DEFECT 2. locusAPI.cls:3439 tests the destination quantity *before* the
        // moved quantity is added at 3456. On a row just created it is 0; on an existing
        // row it is whatever was already there. Either way the check cannot see the change
        // it is meant to guard. Reproduced — moving it below the adjustment would start
        // flagging cycle counts the ABL side never raises, and the two stacks have to agree
        // during the parallel run.
        if (destination.TotalQuantity < 0)
        {
            var flagged = await _cycleCounts.FlagForCountAsync(
                destination.Company,
                destination.Warehouse,
                _context.UserId,
                destination.Id,
                cancellationToken).ConfigureAwait(false);

            if (flagged.Failed)
            {
                // Verified: locusAPI.cls:3445.
                return Fail("Error setting cycle count.");
            }
        }

        await _inventory
            .AdjustQuantityAsync(destination.Id, movedQuantity, cancellationToken)
            .ConfigureAwait(false);

        // --- The stock-adjustment row, when the status changed. ------------------------

        // ABL `ne` on a character field ignores case. A row created above copies the
        // source's status, so this only fires against a shelf row that already existed
        // holding the same item at a different status.
        if (!string.Equals(source.StockStatus, destination.StockStatus, StringComparison.OrdinalIgnoreCase))
        {
            var written = await WriteStockAdjustmentAsync(
                source, destination, movedQuantity, cancellationToken).ConfigureAwait(false);

            // InboundEventResult is a readonly record struct, so the nullable above is a
            // Nullable<T> and has to be unwrapped rather than returned directly.
            if (written is { } adjustmentFailure)
            {
                return adjustmentFailure;
            }
        }

        // ⚠ Exactly zero, not "at or below zero" — locusAPI.cls:3509. A row driven negative
        // is left in place on purpose: the cycle count needs something to count.
        if (sourceTotal == 0)
        {
            await _inventory.DeleteAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }

        // --- The replenishment row. ----------------------------------------------------

        var replenishment = await _audit.AppendAsync(
            new AuditTransaction
            {
                Company = company,
                Warehouse = warehouse,
                TransactionType = Replenishment,
                EmployeeNumber = _context.UserId,
                ItemNumber = movement.ItemNumber,
                // "where to get pallet id?" — the ABL's own comment, line 3536. Left empty.
                PalletId = string.Empty,
                PalletIdFrom = movement.PalletId,
                ItemQuantity = movedQuantity,
                SuggestedQuantity = movedQuantity,
                BinNumber = movement.BinTo,
                BinFrom = movement.BinFrom,
                BinTo = movement.BinTo,
                CaseQuantity = destination.CaseQuantity,
                // Both from the destination — locusAPI.cls:3546-3547 assigns
                // new_inventory.stock_stat to old_stock_stat as well. Not a typo to fix:
                // a replenishment does not change status, so there is no "old" to record.
                StockStatus = destination.StockStatus,
                OldStockStatus = destination.StockStatus,
                Lot = destination.Lot,
                // mach_type is commented out on this row and set on the AS row. Reproduced.
                CustomData =
                [
                    destination.CustomData.ElementAtOrDefault(0),
                    null,
                    null,
                    replenishmentBuiltTime,
                    null,
                ],
            },
            cancellationToken).ConfigureAwait(false);

        if (replenishment.Failed)
        {
            // Verified: locusAPI.cls:3561.
            return Fail("Error in assigning transaction fields.");
        }

        Logger.LogInformation(
            "Replenished {Quantity} of {Item} from {From} to {To} in {Company}/{Warehouse}; " +
            "movement {MovementId} closed.",
            movedQuantity, movement.ItemNumber, movement.BinFrom, movement.BinTo,
            company, warehouse, movement.Id);

        // Verified: locusAPI.cls:3583. The trailing space is in the ABL and on the wire.
        return InboundEventResult.Ok(
            $"PutawayJobResult PUTCOMPLETE successful for carton {licencePlate} ");
    }

    /// <summary>
    /// Builds the new shelf row by copying the source. Converts
    /// <c>locusAPI.cls:3393-3430</c>.
    /// </summary>
    private static Inventory BuildShelfRow(Inventory source, Movement movement) => new()
    {
        Company = source.Company,
        Warehouse = source.Warehouse,
        // A primary pick shelf holds split stock and carries no pallet id.
        PalletId = string.Empty,
        ItemNumber = movement.ItemNumber,
        BinNumber = movement.BinTo,
        // Created at zero; the moved quantity is added afterwards, separately.
        TotalQuantity = 0m,

        NsComment = source.NsComment,
        UnitOfMeasure = source.UnitOfMeasure,
        Expiration = source.Expiration,
        CaseQuantity = source.CaseQuantity,
        TruckId = source.TruckId,
        Lot = source.Lot,
        CycleFlag = source.CycleFlag,
        CycleLevel = source.CycleLevel,
        CycleEmployeeNumber = source.CycleEmployeeNumber,
        CycleId = source.CycleId,
        ReturnCategory = source.ReturnCategory,
        ReturnPalletFull = source.ReturnPalletFull,
        CustomData = [.. source.CustomData],
        Attributes = source.Attributes,
        SuggestedBin = source.SuggestedBin,
        EmployeeNumber = source.EmployeeNumber,
        VendorId = source.VendorId,
        PoNumber = source.PoNumber,
        PoSuffix = source.PoSuffix,
        ItemCost = source.ItemCost,
        CountryCode = source.CountryCode,
        StockStatus = source.StockStatus,

        // ⚠ ABL DEFECT 1. locusAPI.cls:3427 reads:
        //
        //     new_inventory.date_time = old_inventory.date_time
        //         when old_inventory.date_time ne "" and ... and
        //              old_inventory.date_time lt new_inventory.date_time
        //
        // new_inventory was CREATEd four statements earlier and n_invent.t sets only `id`,
        // so its date_time is "". Nothing is less than "". The condition can never hold and
        // the row is written with no timestamp — which is what happens here.
        //
        // This is not cosmetic. date_time is the fourth field of co_wh_abs_fifo, the
        // primary index, so a blank sorts ahead of every dated row: replenished stock
        // always looks like the oldest stock in the bin. Raise it with the warehouse team;
        // do not fix it here, because the ABL side would keep writing blanks and the two
        // stacks would order FIFO differently.
        DateTime = null,
    };

    /// <summary>
    /// Writes the <c>AS</c> row that records a stock-status change. Converts
    /// <c>locusAPI.cls:3459-3504</c>. Returns a failure result, or <see langword="null"/>
    /// on success.
    /// </summary>
    private async Task<InboundEventResult?> WriteStockAdjustmentAsync(
        Inventory source,
        Inventory destination,
        int movedQuantity,
        CancellationToken cancellationToken)
    {
        // 7007 when a status appears where there was none, 7004 for any other change.
        // locusAPI.cls:3463-3474.
        var ruleId =
            string.IsNullOrWhiteSpace(source.StockStatus)
            && !string.IsNullOrWhiteSpace(destination.StockStatus)
                ? RuleStatusAdded
                : RuleStatusChanged;

        var adjustmentCode = await _rules.GetRuleAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);

        var item = await _items.FindAsync(
            _context.Company,
            _context.Warehouse,
            destination.ItemNumber ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        // "N" when the item is not in the master — locusAPI.cls:3480. Note this is not the
        // schema default "S"; the substitution is how the row records "unknown item".
        var itemType = item?.ItemType ?? "N";

        // ⚠ ABL DEFECT 3. locusAPI.cls:3495 calls get_case_qty(inventory.abs_num) against
        // the bare `inventory` buffer, which this method never populates — not
        // new_inventory, not old_inventory. An unavailable buffer yields a blank item
        // number, and static_connect:get_case_qty returns 1 when it finds no item
        // (static_connect.cls:2724). So this is always 1. Reproduced by passing the empty
        // item number the ABL effectively passes.
        var caseQuantity = await _items.GetCaseQuantityAsync(
            _context.Company, _context.Warehouse, string.Empty, cancellationToken)
            .ConfigureAwait(false);

        var result = await _audit.AppendAsync(
            new AuditTransaction
            {
                Company = destination.Company,
                Warehouse = destination.Warehouse,
                TransactionType = StockAdjustment,
                EmployeeNumber = _context.UserId,
                ItemType = itemType,
                ItemNumber = destination.ItemNumber,
                PalletId = destination.PalletId,
                PalletIdFrom = source.PalletId,
                ItemQuantity = movedQuantity,
                SuggestedQuantity = movedQuantity,
                BinNumber = source.BinNumber,
                BinTo = destination.BinNumber,
                AdjustmentCode = adjustmentCode,
                CaseQuantity = caseQuantity,
                StockStatus = destination.StockStatus,
                OldStockStatus = source.StockStatus,
                MachType = _context.UserType,
                // Explicitly blank at locusAPI.cls:3500, though both inventory rows have a
                // lot. Deliberate or not, it is what the ABL writes.
                Lot = string.Empty,
            },
            cancellationToken).ConfigureAwait(false);

        // The ABL does not check for an error on this CREATE — it has no `no-error` and no
        // following test, so a failure would raise and unwind the block. Same outcome here,
        // expressed as a failure result rather than an exception.
        return result.Failed
            ? Fail("Error in assigning transaction fields.")
            : null;
    }

    /// <summary>
    /// Reads <c>ExecQty</c> off the last <c>PUT</c> task in the payload. Converts
    /// <c>locusAPI.cls:3275-3287</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ABL loops over the whole array and assigns <c>iMovedQty</c> on every <c>PUT</c>
    /// task without accumulating, so with more than one <c>PUT</c> the last wins and the
    /// rest are silently dropped. Reproduced. Whether Locus ever sends more than one is a
    /// question for the captured traffic.
    /// </para>
    /// <para>
    /// <c>PutawayJobResultTask</c> is an array, where the order family's
    /// <c>OrderJobResultTask</c> is a single object. That asymmetry is real — see
    /// <c>CLAUDE.md</c>.
    /// </para>
    /// </remarks>
    private static int ReadExecutedQuantity(JsonElement root)
    {
        if (!root.TryGetProperty("JobTasks", out var jobTasks)
            || jobTasks.ValueKind != JsonValueKind.Object
            || !jobTasks.TryGetProperty("PutawayJobResultTask", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var quantity = 0;

        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadString(task, "TaskType"), "PUT", StringComparison.Ordinal))
            {
                continue;
            }

            // ABL `integer("")` and `integer(?)` both yield 0 under NO-ERROR. int.TryParse
            // failing leaves the previous value, matching the ABL's assignment-in-a-loop.
            if (int.TryParse(
                    ReadString(task, "ExecQty"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                quantity = parsed;
            }
        }

        return quantity;
    }

    private InboundEventResult Fail(string body)
    {
        Logger.LogWarning("Locus {EventType}: {Body}", EventType, body);

        // 200 with an error body, not a 4xx — locusAPI.cls:1774-1780.
        return InboundEventResult.ApplicationFailure(body);
    }
}

/// <summary>
/// PUTAWAYPUTCOMPLETE. Converts <c>locusAPI.cls:4486-4492</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ABL method is a four-line delegation to <c>replenputcomplete</c>.</b> It parses
/// nothing, decides nothing, and passes the payload straight through — so a putaway
/// completion and a replenishment completion are the same operation, including the response
/// string, which says <c>PUTCOMPLETE</c> either way.
/// </para>
/// <para>
/// Modelled the same way: this handler holds the inner one and forwards. Duplicating the
/// 345 lines would create two implementations that could drift, and the ABL has exactly one.
/// </para>
/// <para>
/// It sat marked "resolve ownership before converting" alongside <c>replenputcomplete</c>.
/// The stub was right about the seam and wrong about the size — reading it took a minute.
/// That is now the fourth documentation claim in this project found to overstate what the
/// code does.
/// </para>
/// </remarks>
public sealed class LocusPutawayPutCompleteHandler : LocusEventHandlerBase
{
    private readonly LocusReplenPutCompleteHandler _inner;

    public LocusPutawayPutCompleteHandler(
        LocusReplenPutCompleteHandler inner,
        ILogger<LocusPutawayPutCompleteHandler> logger)
        : base(logger) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override string EventType => LocusEventType.PutawayPutComplete;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.HandleAsync(request, cancellationToken);
}
