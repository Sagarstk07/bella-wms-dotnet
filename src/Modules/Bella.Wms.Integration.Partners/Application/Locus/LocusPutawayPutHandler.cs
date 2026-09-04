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
/// Converts <c>locusAPI.cls:4134-4425</c> — <c>releaseReplenishment</c>, 292 lines.
/// </summary>
/// <remarks>
/// <para>
/// Called by <see cref="LocusPutawayPutHandler"/> when the tote turns out to be carrying a
/// replenishment rather than a putaway. It draws down the movement by whatever is actually
/// on the tote, moves the stock, and writes an <c>MR</c> row.
/// </para>
/// <para>
/// <b>Its own class rather than a method</b> because it is a self-contained 292-line
/// operation with three of its own failure modes, and burying it inside a handler would
/// make both unreadable. No interface: it has exactly one caller and inventing a seam here
/// would be pretending at a boundary that does not exist.
/// </para>
/// <para>
/// <b>⚠ Three defects live in this ABL, and one of them corrupts the audit trail.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b><c>btrans.abs_num = new_inventory.emp_num</c></b> (<c>locusAPI.cls:4400</c>). The
/// item number is assigned the <i>employee</i> number. Every <c>MR</c> row this path has
/// ever written records a six-character employee id in a twenty-four-character item column,
/// so replenishments released through here cannot be traced back to what was moved.
/// </description></item>
/// <item><description>
/// <b>Every failure returns <c>false</c> and the caller discards it</b>
/// (<c>3650</c>). By then this has already changed <c>movemst</c> and <c>inventory</c>, and
/// nothing undoes the block — so a half-applied stock move commits. This is the single most
/// dangerous line in the putaway family. See the note on <see cref="LocusPutawayPutHandler"/>.
/// </description></item>
/// <item><description>
/// <b>The cycle-count calls pass an empty employee number</b> (<c>4207, 4331</c>) where
/// every other call site passes the executing user, so a count raised here has no owner.
/// </description></item>
/// </list>
/// <para>
/// <b>And one thing it gets right that its two siblings get wrong.</b> Lines
/// <c>4348-4363</c> copy the FIFO timestamp in an explicit block that handles the blank case
/// properly. <c>replenputcomplete</c> and <c>putawayput</c> attempt the same copy in an
/// <c>ASSIGN … WHEN</c> whose condition can never be true. The working version existing in
/// the same file is the strongest evidence available that the other two are a defect rather
/// than a decision — worth taking to whoever owns FIFO.
/// </para>
/// </remarks>
public sealed class ReplenishmentReleaseService
{
    private const string StockAdjustment = "AS";
    private const string Replenishment = "MR";
    private const int RuleStatusAdded = 7007;
    private const int RuleStatusChanged = 7004;

    private readonly IMovementRepository _movements;
    private readonly IInventoryRepository _inventory;
    private readonly IItemRepository _items;
    private readonly ICycleCountService _cycleCounts;
    private readonly IAuditService _audit;
    private readonly IBusinessRuleService _rules;
    private readonly IWmsContext _context;
    private readonly ILogger<ReplenishmentReleaseService> _logger;

    public ReplenishmentReleaseService(
        IMovementRepository movements,
        IInventoryRepository inventory,
        IItemRepository items,
        ICycleCountService cycleCounts,
        IAuditService audit,
        IBusinessRuleService rules,
        IWmsContext context,
        ILogger<ReplenishmentReleaseService> logger)
    {
        _movements = movements ?? throw new ArgumentNullException(nameof(movements));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _cycleCounts = cycleCounts ?? throw new ArgumentNullException(nameof(cycleCounts));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Releases a replenishment. Returns <see langword="false"/> on any failure, exactly as
    /// the ABL does — including after it has already written.
    /// </summary>
    public async Task<bool> ReleaseAsync(int movementId, CancellationToken cancellationToken)
    {
        var company = _context.Company;
        var warehouse = _context.Warehouse;

        var movement = await _movements
            .FindByIdAsync(company, warehouse, movementId, forUpdate: true, cancellationToken)
            .ConfigureAwait(false);

        if (movement is null)
        {
            _logger.LogWarning("No movemst {MovementId} to release.", movementId);
            return false;
        }

        // The ABL snapshots the movement into a temp-table here and works from the copy for
        // the rest of the method. That matters: the movement's quantity is reduced below,
        // and every later use — the inventory adjustment, both audit rows — reads the
        // *original* value off the snapshot. Holding the record is how that is reproduced.
        var snapshot = movement;

        // "There should only be one tote to this location for this item" — the ABL's own
        // comment, line 4159.
        var onTote = await _inventory.FindByBinAndItemAsync(
            company,
            warehouse,
            movement.BinFrom ?? string.Empty,
            movement.ItemNumber ?? string.Empty,
            forUpdate: false,
            cancellationToken).ConfigureAwait(false);

        if (onTote is null)
        {
            _logger.LogWarning(
                "Nothing on tote {Tote} for item {Item}; movemst {MovementId} not released.",
                movement.BinFrom, movement.ItemNumber, movementId);
            return false;
        }

        // Draw the movement down by what is physically on the tote, and drop it when it
        // reaches zero. locusAPI.cls:4171-4176.
        var remaining = await _movements
            .AdjustQuantityAsync(movementId, -onTote.TotalQuantity, cancellationToken)
            .ConfigureAwait(false);

        if (remaining <= 0)
        {
            await _movements.DeleteAsync(company, warehouse, movementId, cancellationToken)
                .ConfigureAwait(false);
        }

        // --- Source. -------------------------------------------------------------------

        // pallet_id = "" here, unlike putawayput's own loop which drops the pallet from the
        // predicate entirely. locusAPI.cls:4189.
        var source = await _inventory.FindByLocationAsync(
            company,
            warehouse,
            snapshot.BinFrom ?? string.Empty,
            palletId: string.Empty,
            snapshot.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            _logger.LogWarning(
                "Missing inventory in {Bin} for item {Item}; movemst {MovementId} " +
                "half-released — its quantity has already been reduced.",
                snapshot.BinFrom, snapshot.ItemNumber, movementId);
            return false;
        }

        var sourceTotal = await _inventory
            .AdjustQuantityAsync(source.Id, -snapshot.Quantity, cancellationToken)
            .ConfigureAwait(false);

        if (sourceTotal < 0 && !await FlagCountAsync(source, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // --- Destination. --------------------------------------------------------------

        var destination = await _inventory.FindByLocationAsync(
            company,
            warehouse,
            snapshot.BinTo ?? string.Empty,
            palletId: string.Empty,
            snapshot.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (destination is null)
        {
            var created = source with
            {
                PalletId = string.Empty,
                ItemNumber = snapshot.ItemNumber,
                BinNumber = snapshot.BinTo,
                TotalQuantity = 0m,
                // ⚠ The executing user, not the source row's employee. The other two
                // handlers copy old_inventory.emp_num here. locusAPI.cls:4260.
                EmployeeNumber = _context.UserId,
                StockStatus = source.StockStatus,
                // No date_time in the ABL's assign block at all — it is set further down,
                // correctly, by the block this method gets right.
                DateTime = null,
                CustomData = [.. source.CustomData],
            };

            var id = await _inventory.CreateAsync(created, cancellationToken).ConfigureAwait(false);
            destination = created with { Id = id };
        }

        if (!string.Equals(source.StockStatus, destination.StockStatus, StringComparison.OrdinalIgnoreCase)
            && !await WriteStockAdjustmentAsync(
                source, destination, snapshot.Quantity, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // ⚠ Checked before the addition below, the same ordering slip the other two
        // handlers have. locusAPI.cls:4328 vs 4342.
        if (destination.TotalQuantity < 0
            && !await FlagCountAsync(destination, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await _inventory
            .AdjustQuantityAsync(destination.Id, snapshot.Quantity, cancellationToken)
            .ConfigureAwait(false);

        // --- The FIFO timestamp, done properly. ----------------------------------------

        // Converts locusAPI.cls:4348-4363. A blank source timestamp is left alone; a real
        // one is copied when it is older than the destination's, or when the destination
        // has none. This is the version that works — see the class remarks.
        if (!string.IsNullOrEmpty(source.DateTime))
        {
            var shouldCopy =
                string.CompareOrdinal(source.DateTime, destination.DateTime ?? string.Empty) < 0
                || string.IsNullOrEmpty(destination.DateTime);

            if (shouldCopy)
            {
                await _inventory
                    .SetDateTimeAsync(destination.Id, source.DateTime, cancellationToken)
                    .ConfigureAwait(false);

                destination = destination with { DateTime = source.DateTime };
            }
        }

        // Exactly zero, not "at or below". locusAPI.cls:4366.
        if (sourceTotal == 0)
        {
            await _inventory.DeleteAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }

        // --- The MR row. ---------------------------------------------------------------

        var result = await _audit.AppendAsync(
            new AuditTransaction
            {
                Company = destination.Company,
                Warehouse = destination.Warehouse,
                TransactionType = Replenishment,
                EmployeeNumber = destination.EmployeeNumber ?? string.Empty,

                // ⚠⚠ ABL DEFECT, locusAPI.cls:4400: `btrans.abs_num = new_inventory.emp_num`.
                // The item number is assigned the employee number. abs_num is x(24) and
                // emp_num is x(6), so it fits and nothing complains — the row simply records
                // the wrong thing, and has done for as long as this path has run.
                //
                // Reproduced because the ABL side keeps writing it and a reporting query
                // that has learned to work around it would break against corrected rows.
                // This is the clearest candidate for a fix-once-cut-over-is-done list, and
                // it should be on one.
                ItemNumber = destination.EmployeeNumber,

                PalletId = string.Empty,
                PalletIdFrom = snapshot.PalletId,
                ItemQuantity = snapshot.Quantity,
                SuggestedQuantity = snapshot.Quantity,
                BinNumber = destination.BinNumber,
                BinFrom = snapshot.BinFrom,
                BinTo = destination.BinNumber,
                CaseQuantity = destination.CaseQuantity,
                StockStatus = destination.StockStatus,
                OldStockStatus = destination.StockStatus,
                Lot = destination.Lot,
                CustomData =
                [
                    destination.CustomData.ElementAtOrDefault(0),
                    null,
                    null,
                    snapshot.CustomData.ElementAtOrDefault(3),
                    null,
                ],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            _logger.LogError(
                "Failed to write the MR row for movemst {MovementId}: {Error}",
                movementId, result.Message);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts the two <c>rf/wsset_cyc.p</c> calls at <c>locusAPI.cls:4204</c> and
    /// <c>4328</c>.
    /// </summary>
    private async Task<bool> FlagCountAsync(Inventory row, CancellationToken cancellationToken)
    {
        // ⚠ The ABL passes "" as the employee — not cExecUser, which every other call site
        // in this file uses. A cycle count raised from here has no owner. Reproduced: a
        // count attributed to the wrong person is worse than one attributed to nobody, and
        // the warehouse team can say which they want.
        var flagged = await _cycleCounts.FlagForCountAsync(
            row.Company, row.Warehouse, string.Empty, row.Id, cancellationToken)
            .ConfigureAwait(false);

        if (flagged.Failed)
        {
            _logger.LogError(
                "Error setting cycle count on inventory {Id}: {Error}", row.Id, flagged.Message);
        }

        return !flagged.Failed;
    }

    /// <summary>Converts <c>locusAPI.cls:4272-4324</c>.</summary>
    private async Task<bool> WriteStockAdjustmentAsync(
        Inventory source,
        Inventory destination,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var ruleId =
            string.IsNullOrWhiteSpace(source.StockStatus)
            && !string.IsNullOrWhiteSpace(destination.StockStatus)
                ? RuleStatusAdded
                : RuleStatusChanged;

        var item = await _items.FindAsync(
            _context.Company,
            _context.Warehouse,
            destination.ItemNumber ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        // ⚠ Same unpopulated-buffer defect as replenputcomplete — locusAPI.cls:4316 reads
        // the bare `inventory` buffer, which this method never populates. Always 1.
        var caseQuantity = await _items
            .GetCaseQuantityAsync(_context.Company, _context.Warehouse, string.Empty, cancellationToken)
            .ConfigureAwait(false);

        var result = await _audit.AppendAsync(
            new AuditTransaction
            {
                Company = destination.Company,
                Warehouse = destination.Warehouse,
                TransactionType = StockAdjustment,
                EmployeeNumber = _context.UserId,
                ItemType = item?.ItemType ?? "N",
                ItemNumber = destination.ItemNumber,
                PalletId = destination.PalletId,
                PalletIdFrom = source.PalletId,
                ItemQuantity = quantity,
                SuggestedQuantity = quantity,
                BinNumber = source.BinNumber,
                BinTo = destination.BinNumber,
                AdjustmentCode = await _rules.GetRuleAsync(ruleId, cancellationToken).ConfigureAwait(false),
                CaseQuantity = caseQuantity,
                StockStatus = destination.StockStatus,
                OldStockStatus = source.StockStatus,
                MachType = _context.UserType,
                Lot = string.Empty,
            },
            cancellationToken).ConfigureAwait(false);

        return !result.Failed;
    }
}

/// <summary>
/// PUTAWAYPUT — a Locus robot reports what it put away off a tote. Converts
/// <c>locusAPI.cls:3589-3893</c> (304 lines).
/// </summary>
/// <remarks>
/// <para>
/// <b>One event, two entirely different operations, chosen by whether a row exists.</b> If
/// an open <c>movemst</c> is found against the tote, this is a replenishment and
/// <see cref="ReplenishmentReleaseService"/> handles all of it. Otherwise it is a putaway,
/// and the handler walks every item on the tote and moves each one to the task location.
/// Nothing in the payload distinguishes the two cases.
/// </para>
/// <para>
/// <b>The payload shape contradicts its sibling.</b> Here <c>PutawayJobResultTask</c> is
/// read as a single object (<c>3619</c>); in <c>replenputcomplete</c> the same property is
/// read as an array (<c>3271</c>). Both are in the same class, on the same message family,
/// forty lines apart. Whatever Locus actually sends, one of these is wrong — and until
/// captured traffic says which, both are reproduced as written.
/// </para>
/// <para>
/// <b>⚠ Defects reproduced here</b>, on top of the two this shares with
/// <c>replenputcomplete</c> (the dead FIFO-timestamp copy and the pre-add cycle-count check):
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The "not available" message names an unpopulated buffer</b> (<c>3671</c>):
/// <c>"Inventory record on robot " + transactions.bin_from</c>, where <c>transactions</c> is
/// never read in this method. The message that reaches Locus has a blank where the robot
/// should be. Unlike the other buffer defects this one is <i>on the wire</i>.
/// </description></item>
/// <item><description>
/// <b>The exception-code conditions can never be false</b> (<c>3818, 3821, 3824</c>):
/// <c>IF x ne ? or x ne ""</c> is true for every possible value of <c>x</c> — it wants
/// <c>and</c>. So <c>comments</c> is always written, and a task with no exception at all
/// gets the literal string <c>"|"</c>. Every audit row this handler writes carries it.
/// </description></item>
/// <item><description>
/// <b><c>releaseReplenishment</c>'s return value is discarded</b> (<c>3650</c>). It returns
/// <c>false</c> from six places, several of them after it has already written, and the block
/// commits regardless. <b>Reproduced, and this is the one to argue about.</b> The house rule
/// is to preserve ABL behaviour because Locus's retry logic may depend on it — but a partial
/// stock move is not wire behaviour, it is a corrupt shelf. The failure is logged at Error
/// with the movement id so it is at least findable, which the ABL's <c>message</c> statement
/// is not: in PASOE it goes nowhere.
/// </description></item>
/// <item><description>
/// <b>The same quantity is subtracted from every item on the tote</b> (<c>3676</c>). The
/// loop walks each item and takes <c>ExecQty</c> — one number from the payload — off all of
/// them. If that is right, a tote only ever holds one item and the loop is defensive; if it
/// is wrong, a multi-item tote loses stock it never had. Worth a warehouse answer.
/// </description></item>
/// </list>
/// <para>
/// One more thing this does that its sibling does not: new shelf rows are created with a
/// <b>blank</b> <c>stock_stat</c> (<c>3733</c>, marked <c>BEL010017</c>) rather than the
/// source's. That makes the status-change test below it fire for every created row.
/// </para>
/// </remarks>
public sealed class LocusPutawayPutHandler : LocusEventHandlerBase
{
    /// <summary><c>{&amp;T_SADJ}</c> — stock adjustment. <c>globals/globals.i:163</c>.</summary>
    private const string StockAdjustment = "AS";

    /// <summary><c>{&amp;T_MVSM}</c> — stock movement. <c>globals/globals.i:193</c>.</summary>
    private const string StockMovement = "MS";

    private const int RuleStatusAdded = 7007;
    private const int RuleStatusChanged = 7004;

    private readonly IMovementRepository _movements;
    private readonly IInventoryRepository _inventory;
    private readonly IItemRepository _items;
    private readonly ICycleCountService _cycleCounts;
    private readonly IAuditService _audit;
    private readonly IBusinessRuleService _rules;
    private readonly ReplenishmentReleaseService _release;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    public LocusPutawayPutHandler(
        IMovementRepository movements,
        IInventoryRepository inventory,
        IItemRepository items,
        ICycleCountService cycleCounts,
        IAuditService audit,
        IBusinessRuleService rules,
        ReplenishmentReleaseService release,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusPutawayPutHandler> logger)
        : base(logger)
    {
        _movements = movements ?? throw new ArgumentNullException(nameof(movements));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _cycleCounts = cycleCounts ?? throw new ArgumentNullException(nameof(cycleCounts));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // movemst (3641) before inventory (3655) before transactions (3805). Same order as
        // replenputcomplete, which matters: the two run against the same tables and a
        // mismatch deadlocks under load.
        _lockOrder = LockOrder.Declare(
            "locus putawayput",
            "api/wms/locusAPI.cls:3589-3893",
            "movemst",
            "inventory",
            "transactions",
            "item");
    }

    public override string EventType => LocusEventType.PutawayPut;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);

        var task = ReadTask(document.RootElement);

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await PutAwayAsync(task, cancellationToken).ConfigureAwait(false);

            if (result.HandlerSucceeded)
            {
                await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
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

    private async Task<InboundEventResult> PutAwayAsync(
        PutawayTask task,
        CancellationToken cancellationToken)
    {
        var company = _context.Company;
        var warehouse = _context.Warehouse;

        // "Check for replen first" — the ABL's own comment, line 3640.
        var replenishment = await _movements
            .FindOpenByToteAsync(company, warehouse, task.ToteId, cancellationToken)
            .ConfigureAwait(false);

        if (replenishment is not null)
        {
            var released = await _release
                .ReleaseAsync(replenishment.Id, cancellationToken)
                .ConfigureAwait(false);

            if (!released)
            {
                // ⚠ ABL DEFECT 3. locusAPI.cls:3650 discards this. The block commits and
                // Locus is told the job succeeded, even though the release may have stopped
                // half-way through with movemst already drawn down. Reproduced — but logged
                // at Error, which the ABL's `message` statement is not: under PASOE that
                // output has nowhere to go.
                Logger.LogError(
                    "releaseReplenishment failed for movemst {MovementId} on tote {Tote} " +
                    "({Company}/{Warehouse}). The ABL discards this result and commits, so " +
                    "the stock move may be half-applied. Check movemst {MovementId} and the " +
                    "inventory in {Bin} before trusting either.",
                    replenishment.Id, task.ToteId, company, warehouse,
                    replenishment.Id, replenishment.BinFrom);
            }

            // Falls through to the same success response either way — the replenishment
            // branch and the putaway branch share one exit. locusAPI.cls:3878.
            return Success(task.ToteId);
        }

        // --- Putaway: walk everything on the tote. -------------------------------------

        var onTote = await _inventory
            .FindByBinAsync(company, warehouse, task.ToteId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in onTote)
        {
            var moved = await MoveOneAsync(task, row, cancellationToken).ConfigureAwait(false);

            if (moved is { } failure)
            {
                return failure;
            }
        }

        // An empty tote is not an error. The FOR EACH simply does not run and the ABL
        // reports success — so a putaway against a tote nothing is on is acknowledged.
        return Success(task.ToteId);
    }

    /// <summary>Converts one pass of the <c>FOR EACH</c> at <c>locusAPI.cls:3655-3874</c>.</summary>
    private async Task<InboundEventResult?> MoveOneAsync(
        PutawayTask task,
        Inventory onTote,
        CancellationToken cancellationToken)
    {
        var company = _context.Company;
        var warehouse = _context.Warehouse;

        // No pallet in the predicate — unlike releaseReplenishment, which pins it to "".
        var source = await _inventory.FindByBinAndItemAsync(
            company,
            warehouse,
            task.ToteId,
            onTote.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            // ⚠ ABL DEFECT 1, and this one is on the wire. locusAPI.cls:3671 builds the
            // message from `transactions.bin_from` — a buffer this method never reads — so
            // the robot id is always blank and the string has two spaces in the middle.
            // Reproduced exactly: Locus has been receiving this for years and anything
            // matching on it would break if it were corrected.
            return Fail("Inventory record on robot  is not available");
        }

        var sourceTotal = await _inventory
            .AdjustQuantityAsync(source.Id, -task.MovedQuantity, cancellationToken)
            .ConfigureAwait(false);

        if (sourceTotal < 0)
        {
            var flagged = await _cycleCounts.FlagForCountAsync(
                source.Company, source.Warehouse, _context.UserId, source.Id, cancellationToken)
                .ConfigureAwait(false);

            if (flagged.Failed)
            {
                return Fail("Error setting cycle count.");
            }
        }

        var destination = await _inventory.FindByLocationAsync(
            company,
            warehouse,
            task.TaskLocation,
            palletId: string.Empty,
            onTote.ItemNumber ?? string.Empty,
            forUpdate: true,
            cancellationToken).ConfigureAwait(false);

        if (destination is null)
        {
            var created = source with
            {
                PalletId = string.Empty,
                ItemNumber = onTote.ItemNumber,
                BinNumber = task.TaskLocation,
                TotalQuantity = 0m,
                CustomData = [.. source.CustomData],

                // ⚠ Blank, not copied — locusAPI.cls:3733, marked BEL010017. This is the
                // one field where putawayput and replenputcomplete deliberately differ, and
                // it makes the status-change test below fire for every row created here.
                StockStatus = string.Empty,

                // ⚠ ABL DEFECT (shared). The `when` guard at 3735 can never be true — see
                // LocusReplenPutCompleteHandler. Written with no FIFO timestamp.
                DateTime = null,
            };

            var id = await _inventory.CreateAsync(created, cancellationToken).ConfigureAwait(false);
            destination = created with { Id = id };
        }

        // ⚠ ABL DEFECT (shared). Checked before the addition below. locusAPI.cls:3757.
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
                return Fail("Error setting cycle count.");
            }
        }

        await _inventory
            .AdjustQuantityAsync(destination.Id, task.MovedQuantity, cancellationToken)
            .ConfigureAwait(false);

        var item = await _items.FindAsync(
            company, warehouse, onTote.ItemNumber ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        var itemType = item?.ItemType ?? "N";

        if (!string.Equals(source.StockStatus, destination.StockStatus, StringComparison.OrdinalIgnoreCase))
        {
            var ruleId =
                string.IsNullOrWhiteSpace(source.StockStatus)
                && !string.IsNullOrWhiteSpace(destination.StockStatus)
                    ? RuleStatusAdded
                    : RuleStatusChanged;

            // Unlike replenputcomplete's equivalent, this one passes a populated item
            // number, so the case quantity is real rather than always 1.
            var caseQuantity = await _items.GetCaseQuantityAsync(
                company, warehouse, onTote.ItemNumber ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);

            var adjustment = await _audit.AppendAsync(
                new AuditTransaction
                {
                    Company = company,
                    Warehouse = warehouse,
                    TransactionType = StockAdjustment,
                    EmployeeNumber = task.ExecutingUser,
                    ItemType = itemType,
                    ItemNumber = onTote.ItemNumber,
                    PalletId = onTote.PalletId,
                    ItemQuantity = task.MovedQuantity,
                    SuggestedQuantity = task.MovedQuantity,
                    BinNumber = task.ToteId,
                    BinTo = task.TaskLocation,
                    AdjustmentCode = await _rules.GetRuleAsync(ruleId, cancellationToken).ConfigureAwait(false),
                    CaseQuantity = caseQuantity,
                    StockStatus = destination.StockStatus,
                    OldStockStatus = source.StockStatus,
                    MachType = _context.UserType,
                    Lot = string.Empty,
                    Comments = task.Comments,
                },
                cancellationToken).ConfigureAwait(false);

            if (adjustment.Failed)
            {
                return Fail("Error in assigning transaction fields.");
            }
        }

        var movement = await _audit.AppendAsync(
            new AuditTransaction
            {
                Company = company,
                Warehouse = warehouse,
                TransactionType = StockMovement,
                EmployeeNumber = task.ExecutingUser,
                ItemNumber = onTote.ItemNumber,
                PalletId = onTote.PalletId,
                ItemQuantity = task.MovedQuantity,
                SuggestedQuantity = task.MovedQuantity,
                BinFrom = task.ToteId,
                BinTo = task.TaskLocation,
                ShiftNumber = _context.ShiftNumber,
                Comments = task.Comments,
            },
            cancellationToken).ConfigureAwait(false);

        if (movement.Failed)
        {
            return Fail("Error in assigning transaction fields.");
        }

        // Exactly zero. locusAPI.cls:3865.
        if (sourceTotal == 0)
        {
            await _inventory.DeleteAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Reads the single <c>PutawayJobResultTask</c> object. Converts
    /// <c>locusAPI.cls:3616-3631</c>.
    /// </summary>
    private static PutawayTask ReadTask(JsonElement root)
    {
        var task = default(JsonElement);

        if (root.TryGetProperty("JobTasks", out var jobTasks)
            && jobTasks.ValueKind == JsonValueKind.Object
            && jobTasks.TryGetProperty("PutawayJobResultTask", out var found)
            && found.ValueKind == JsonValueKind.Object)
        {
            task = found;
        }

        if (task.ValueKind != JsonValueKind.Object)
        {
            // Every field stays at ABL's initial value for a character variable: "". The
            // method then runs to completion against a blank tote and reports success,
            // which is what the ABL does — parseJSOProperty is wrapped in NO-ERROR.
            return new PutawayTask(string.Empty, string.Empty, 0, string.Empty, BuildComments(null, null));
        }

        // TaskType is parsed at line 3621 and never used. Not read here.
        _ = int.TryParse(
            ReadString(task, "ExecQty"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var quantity);

        return new PutawayTask(
            ToteId: ReadString(task, "JobTaskId") ?? string.Empty,
            TaskLocation: ReadString(task, "TaskLocation") ?? string.Empty,
            MovedQuantity: quantity,
            ExecutingUser: ReadString(task, "ExecUser") ?? string.Empty,
            Comments: BuildComments(
                task.TryGetProperty("ExceptionCode", out _) ? ReadString(task, "ExceptionCode") : null,
                task.TryGetProperty("ExceptionReason", out _) ? ReadString(task, "ExceptionReason") : null));
    }

    /// <summary>
    /// Builds <c>transactions.comments</c> from the exception fields. Converts
    /// <c>locusAPI.cls:3817-3830</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ ABL DEFECT 2. The ABL guards all three assignments with
    /// <c>IF x ne ? or x ne ""</c>, which is true for every value <c>x</c> can hold — if it
    /// is unknown the second half is true, and if it is blank the first half is. It wants
    /// <c>and</c>. So all three branches always run and the result is unconditionally
    /// <c>code + "|" + reason</c>, which for a task with no exception is the bare string
    /// <c>"|"</c>. Reproduced: it is on every audit row this handler has ever written, and
    /// anything reading those rows has had to cope with it.
    /// </remarks>
    private static string BuildComments(string? exceptionCode, string? exceptionReason) =>
        $"{exceptionCode}|{exceptionReason}";

    private static InboundEventResult Success(string toteId) =>
        // Verified: locusAPI.cls:3878. Says PUTCOMPLETE, not PUT, and carries the trailing
        // space — the same string replenputcomplete returns.
        InboundEventResult.Ok($"PutawayJobResult PUTCOMPLETE successful for carton {toteId} ");

    private InboundEventResult Fail(string body)
    {
        Logger.LogWarning("Locus {EventType}: {Body}", EventType, body);
        return InboundEventResult.ApplicationFailure(body);
    }

    /// <summary>The five payload fields this handler reads, plus the derived comments.</summary>
    private sealed record PutawayTask(
        string ToteId,
        string TaskLocation,
        int MovedQuantity,
        string ExecutingUser,
        string Comments);
}
