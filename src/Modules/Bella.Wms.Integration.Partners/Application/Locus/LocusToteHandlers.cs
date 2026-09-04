using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// TOTEINDUCTCANCEL — Locus abandons a tote induction. Converts
/// <c>locusAPI.cls:2469-2542</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two writes, both undoing what <c>toteinduct</c> did: close the open <c>JT</c>
/// transactions for this tote and carton, and clear the robot and tote ids from every pick
/// on the carton.
/// </para>
/// <para>
/// <b>Every pick, not just the open ones.</b> <c>toteinduct</c> filters on
/// <c>pick_status eq "o"</c> (<c>2300</c>); this does not (<c>2521</c>). The asymmetry is
/// deliberate — induct claims only picks still open, but a cancel has to release whatever
/// was claimed, including picks that have moved on since. Do not make the two consistent.
/// </para>
/// <para>
/// <b>No audit row is written.</b> Unlike the reject family, this handler records nothing
/// new — it only closes existing rows. That is what the ABL does, and adding a
/// <c>JE</c> row "for completeness" would put entries in the audit trail the ABL side
/// never produces.
/// </para>
/// </remarks>
public sealed class LocusToteInductCancelHandler : LocusEventHandlerBase
{
    private readonly ICartonRepository _cartons;
    private readonly IPickRepository _picks;
    private readonly IToteTransactionRepository _toteTransactions;
    private readonly ILocusClient _locus;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    public LocusToteInductCancelHandler(
        ICartonRepository cartons,
        IPickRepository picks,
        IToteTransactionRepository toteTransactions,
        ILocusClient locus,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusToteInductCancelHandler> logger)
        : base(logger)
    {
        _cartons = cartons ?? throw new ArgumentNullException(nameof(cartons));
        _picks = picks ?? throw new ArgumentNullException(nameof(picks));
        _toteTransactions = toteTransactions ?? throw new ArgumentNullException(nameof(toteTransactions));
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // transactions before pick, matching the ABL's statement order at 2502 and 2521.
        // Both stacks run against one database during the parallel run, so taking these
        // in the other order would deadlock against a concurrent toteinduct.
        _lockOrder = LockOrder.Declare(
            "locus toteinductcancel",
            "api/wms/locusAPI.cls:2469-2542",
            "transactions",
            "pick");
    }

    public override string EventType => LocusEventType.ToteInductCancel;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        // The ABL parses JobStatus and JobDate here too and never uses them.
        var jobId = ReadString(root, "JobId") ?? string.Empty;
        var toteId = ReadString(root, "ToteId") ?? string.Empty;

        var carton = await _cartons
            .FindByCartonIdAsync(_context.Company, _context.Warehouse, jobId, cancellationToken)
            .ConfigureAwait(false);

        if (carton is null)
        {
            var notFoundBody = $"Unable to find carton matching job id {jobId}";

            // This handler does hold the job — unlike holdreject, whose call is commented
            // out. See locusAPI.cls:2498.
            await _locus.HoldOrderJobAsync(jobId, notFoundBody, cancellationToken).ConfigureAwait(false);

            Logger.LogWarning("Locus {EventType}: {Body}", EventType, notFoundBody);
            return InboundEventResult.ApplicationFailure(notFoundBody);
        }

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            var closed = await _toteTransactions.CloseOpenAsync(
                _context.Company,
                _context.Warehouse,
                toteId,
                // Scoped to this carton — locusAPI.cls:2507 adds carton_id to the filter,
                // where toteinduct's equivalent query does not.
                cartonId: jobId,
                comments: "ToteInductCancel",
                cancellationToken).ConfigureAwait(false);

            var released = await _picks.SetToteAssignmentAsync(
                carton.Company,
                carton.Warehouse,
                carton.CartonId ?? jobId,
                jobRobot: string.Empty,
                toteId: string.Empty,
                openPicksOnly: false,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "Tote {ToteId} released from carton {CartonId}: {Closed} JT rows closed, " +
                "{Released} picks cleared.",
                toteId, jobId, closed, released);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Verified: locusAPI.cls:2540.
        return InboundEventResult.Ok(
            $"OrderJobResult tote induct cancel successful for job id {jobId}");
    }
}

/// <summary>
/// TOTEMOVE — Locus moves a carton's work into a different tote. Converts
/// <c>locusAPI.cls:2347-2467</c>.
/// </summary>
/// <remarks>
/// <para>
/// Shares <c>toteinductcancel</c>'s shape but adds the duplicate-tote guard, and its two
/// writes point the other way: instead of releasing picks and closing <c>JT</c> rows, it
/// stamps the new tote onto both.
/// </para>
/// <para>
/// <b>The second write is selected by order, not carton.</b> Picks are updated for the
/// carton in the request; the <c>JT</c> rows are re-linked for every carton on the
/// <i>order</i> (<c>locusAPI.cls:2448-2450</c>). That asymmetry appears deliberate — it is
/// presumably why a move exists as a separate event rather than an induct against a new
/// tote.
/// </para>
/// <para>
/// <b>And every pick, not just open ones</b> — the same divergence from <c>toteinduct</c>
/// that <c>toteinductcancel</c> has. See <see cref="IPickRepository.SetToteAssignmentAsync"/>.
/// </para>
/// </remarks>
public sealed class LocusToteMoveHandler : LocusEventHandlerBase
{
    private readonly ICartonRepository _cartons;
    private readonly IPickRepository _picks;
    private readonly IToteTransactionRepository _toteTransactions;
    private readonly ILocusClient _locus;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    public LocusToteMoveHandler(
        ICartonRepository cartons,
        IPickRepository picks,
        IToteTransactionRepository toteTransactions,
        ILocusClient locus,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusToteMoveHandler> logger)
        : base(logger)
    {
        _cartons = cartons ?? throw new ArgumentNullException(nameof(cartons));
        _picks = picks ?? throw new ArgumentNullException(nameof(picks));
        _toteTransactions = toteTransactions ?? throw new ArgumentNullException(nameof(toteTransactions));
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // pick before transactions here — locusAPI.cls writes picks at 2430 and the JT
        // rows at 2446, the opposite order to toteinductcancel. Both orders are declared
        // as the ABL has them, because the two stacks share one database during the
        // parallel run and inventing a consistent order would deadlock against the ABL.
        _lockOrder = LockOrder.Declare(
            "locus totemove",
            "api/wms/locusAPI.cls:2347-2467",
            "pick",
            "transactions");
    }

    public override string EventType => LocusEventType.ToteMove;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        var jobId = ReadString(root, "JobId") ?? string.Empty;
        var toteId = ReadString(root, "ToteId") ?? string.Empty;
        var jobRobot = ReadString(root, "JobRobot") ?? string.Empty;

        var carton = await _cartons
            .FindByCartonIdAsync(_context.Company, _context.Warehouse, jobId, cancellationToken)
            .ConfigureAwait(false);

        if (carton is null)
        {
            var notFoundBody = $"Unable to find carton matching job id {jobId}";
            await _locus.HoldOrderJobAsync(jobId, notFoundBody, cancellationToken).ConfigureAwait(false);

            Logger.LogWarning("Locus {EventType}: {Body}", EventType, notFoundBody);
            return InboundEventResult.ApplicationFailure(notFoundBody);
        }

        var singleUnit = await _cartons.IsSingleUnitOrderAsync(
            carton.Company,
            carton.Warehouse,
            carton.Order ?? string.Empty,
            carton.OrderSuffix ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        var openTote = await _toteTransactions
            .FindOpenByToteAsync(_context.Company, _context.Warehouse, toteId, cancellationToken)
            .ConfigureAwait(false);

        // The guard, verbatim from locusAPI.cls:2421:
        //   available AND carton_id <> jobId AND (NOT void OR (void AND NOT singleUnit))
        //
        // On a JT row `void` does not mean deleted — toteinduct assigns it lSingleUnit
        // (2327). So the condition reduces to: the tote already holds a DIFFERENT carton,
        // and either that carton is not a single-unit order, or this one is not. Two
        // single-unit orders are allowed to share a tote; anything else is a clash.
        if (openTote is { } inUse
            && !string.Equals(inUse.CartonId, jobId, StringComparison.OrdinalIgnoreCase)
            && (!inUse.IsVoid || !singleUnit))
        {
            var body = $"Unable to induct tote {toteId}. Tote is still in process for {inUse.CartonId}";

            // Tells Locus the tote is taken. Note it reports the carton from THIS request,
            // not the one occupying the tote — locusAPI.cls:2425.
            await _locus.SendDuplicateToteAsync(
                carton.CartonId ?? jobId, singleUnit, cancellationToken).ConfigureAwait(false);

            Logger.LogWarning("Locus {EventType}: {Body}", EventType, body);
            return InboundEventResult.ApplicationFailure(body);
        }

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            var assigned = await _picks.SetToteAssignmentAsync(
                carton.Company,
                carton.Warehouse,
                carton.CartonId ?? jobId,
                jobRobot,
                toteId,
                openPicksOnly: false,
                cancellationToken).ConfigureAwait(false);

            var relinked = await _toteTransactions.RelinkOpenByOrderAsync(
                carton.Company,
                carton.Warehouse,
                carton.Order ?? string.Empty,
                carton.OrderSuffix ?? string.Empty,
                toteId,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "Carton {CartonId} moved to tote {ToteId}: {Assigned} picks, {Relinked} JT rows.",
                jobId, toteId, assigned, relinked);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Verified: locusAPI.cls:2465.
        return InboundEventResult.Ok(
            $"OrderJobResult tote move successful for job id {jobId}");
    }
}

/// <summary>
/// TOTEINDUCT — Locus claims a tote for a carton's picks. Converts
/// <c>locusAPI.cls:2170-2346</c>.
/// </summary>
/// <remarks>
/// <para>
/// The largest of the three tote handlers. Determine whether the order is single-unit,
/// resolve any tote already in use, then stamp the tote onto the open picks and open a new
/// <c>JT</c> transaction to record the association.
/// </para>
/// <para>
/// <b>Its duplicate-tote guard is not the same as <c>totemove</c>\'s.</b> This one has no
/// <c>carton_id ne jobId</c> term (compare <c>2246</c> with <c>2421</c>), and reaching the
/// guard does not mean rejection — it means "look closer": one open row is closed and the
/// induct proceeds; several open rows is the error case. That is a genuinely different
/// shape and merging the two would change behaviour.
/// </para>
/// <para>
/// <b>Only open picks are claimed</b> — <c>pick_status eq "o"</c> at <c>2300</c>, where
/// the other two handlers take every pick on the carton.
/// </para>
/// </remarks>
public sealed class LocusToteInductHandler : LocusEventHandlerBase
{
    private readonly ICartonRepository _cartons;
    private readonly IPickRepository _picks;
    private readonly IToteTransactionRepository _toteTransactions;
    private readonly IAuditService _audit;
    private readonly ILocusClient _locus;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    public LocusToteInductHandler(
        ICartonRepository cartons,
        IPickRepository picks,
        IToteTransactionRepository toteTransactions,
        IAuditService audit,
        ILocusClient locus,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusToteInductHandler> logger)
        : base(logger)
    {
        _cartons = cartons ?? throw new ArgumentNullException(nameof(cartons));
        _picks = picks ?? throw new ArgumentNullException(nameof(picks));
        _toteTransactions = toteTransactions ?? throw new ArgumentNullException(nameof(toteTransactions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        _lockOrder = LockOrder.Declare(
            "locus toteinduct",
            "api/wms/locusAPI.cls:2170-2346",
            "transactions",
            "pick");
    }

    public override string EventType => LocusEventType.ToteInduct;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        var jobId = ReadString(root, "JobId") ?? string.Empty;
        var toteId = ReadString(root, "ToteId") ?? string.Empty;
        var jobRobot = ReadString(root, "JobRobot") ?? string.Empty;

        var carton = await _cartons
            .FindByCartonIdAsync(_context.Company, _context.Warehouse, jobId, cancellationToken)
            .ConfigureAwait(false);

        if (carton is null)
        {
            var notFoundBody = $"Unable to find carton matching job id {jobId}";
            await _locus.HoldOrderJobAsync(jobId, notFoundBody, cancellationToken).ConfigureAwait(false);

            Logger.LogWarning("Locus {EventType}: {Body}", EventType, notFoundBody);
            return InboundEventResult.ApplicationFailure(notFoundBody);
        }

        var singleUnit = await _cartons.IsSingleUnitOrderAsync(
            carton.Company,
            carton.Warehouse,
            carton.Order ?? string.Empty,
            carton.OrderSuffix ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        var openTote = await _toteTransactions
            .FindOpenByToteAsync(_context.Company, _context.Warehouse, toteId, cancellationToken)
            .ConfigureAwait(false);

        // locusAPI.cls:2246 — note there is NO carton_id comparison here, unlike totemove.
        // `void` on a JT row carries the single-unit flag, not deletion (assigned at 2327).
        var needsRelease = openTote is { } inUse && (!inUse.IsVoid || !singleUnit);

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            if (needsRelease)
            {
                // The ABL asserts a single open row by re-running the query with a bare
                // FIND and checking whether the buffer came back available (2252-2259).
                // Closing all matches and counting gives the same decision in one write.
                var closed = await _toteTransactions.CloseOpenAsync(
                    _context.Company,
                    _context.Warehouse,
                    toteId,
                    cartonId: null,   // every open row for the tote, not just this carton
                    comments: "ToteInduct",
                    cancellationToken).ConfigureAwait(false);

                if (closed > 1)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);

                    var occupant = openTote?.CartonId;
                    var body = $"Unable to induct tote {toteId}. Tote is still in process for {occupant}";

                    await _locus.SendDuplicateToteAsync(
                        carton.CartonId ?? jobId, singleUnit, cancellationToken).ConfigureAwait(false);

                    Logger.LogWarning(
                        "Locus {EventType}: {Count} open JT rows for tote {ToteId}. {Body}",
                        EventType, closed, toteId, body);

                    return InboundEventResult.ApplicationFailure(body);
                }
            }

            var claimed = await _picks.SetToteAssignmentAsync(
                carton.Company,
                carton.Warehouse,
                carton.CartonId ?? jobId,
                jobRobot,
                toteId,
                // ⚠ Only open picks — locusAPI.cls:2300. totemove and toteinductcancel
                // take every pick on the carton. Do not make these consistent.
                openPicksOnly: true,
                cancellationToken).ConfigureAwait(false);

            // The item number comes from the carton\'s sole detail line, and is simply
            // omitted when the carton has none or several (locusAPI.cls:2331).
            var itemNumber = carton.CartonNumber is { } cartonNumber
                ? await _cartons.FindSoleDetailItemAsync(cartonNumber, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            await _audit.AppendAsync(
                new AuditTransaction
                {
                    Company = carton.Company,
                    Warehouse = carton.Warehouse,
                    TransactionType = "JT",
                    EmployeeNumber = _context.UserId,
                    CartonId = carton.CartonId,
                    OrderNumber = carton.Order,
                    OrderSuffix = carton.OrderSuffix,
                    Comments = "ToteID",
                    LinkId = toteId,
                    RowStatus = "O",
                    // ⚠ Not a deletion flag. locusAPI.cls:2327 assigns lSingleUnit here,
                    // and every later duplicate-tote check reads it back as such.
                    IsVoid = singleUnit,
                    ItemNumber = itemNumber,
                },
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "Tote {ToteId} inducted for carton {CartonId}: {Claimed} picks claimed, " +
                "single unit {SingleUnit}.",
                toteId, jobId, claimed, singleUnit);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Verified: locusAPI.cls:2344.
        return InboundEventResult.Ok(
            $"OrderJobResult tote induct successful for job id {jobId}");
    }
}
