using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// Shared shape for the reject family (<c>docs/HANDLER_SPECS.md</c> Group 2):
/// <c>reject</c>, <c>holdreject</c>, <c>holdreleasereject</c>, <c>cancelreject</c>.
/// </summary>
/// <remarks>
/// <para>
/// Find <c>cartonmst</c> <c>NO-LOCK</c>; hold the job at Locus and return 200 if it is
/// missing; short-circuit with 200 if the carton is already <c>C</c> or <c>E</c>
/// (idempotency guard); otherwise, inside a transaction, re-find <c>EXCLUSIVE-LOCK</c>,
/// write a <c>JE</c> audit row, and conditionally set <c>row_status</c> to <c>E</c>; then
/// send an alert email and return 200. The spec calls this "one shape, four handlers" —
/// each subclass supplies only its strings.
/// </para>
/// <para>
/// <b>Lock order.</b> The ABL acquires its <c>cartonmst</c> lock at the
/// <c>EXCLUSIVE-LOCK</c> re-find, before it inserts the audit row into
/// <c>transactions</c> — even though the <c>row_status</c> write itself comes later in
/// the ABL's statement order. This handler folds the re-find and the conditional write
/// into one atomic statement
/// (<see cref="ICartonRepository.MarkErrorUnlessCompleteAsync"/>) and calls it before the
/// audit insert, to acquire locks in the same order the ABL does. Calling them in the
/// order the prose lists — audit first — would deadlock against ABL under parallel run
/// (Phase 6 §11).
/// </para>
/// <para>
/// <b>Strings verified against the ABL, 2026-09-01.</b> An earlier draft of
/// <c>docs/HANDLER_SPECS.md</c> paraphrased four of these as "hold-reject wording" /
/// "cancel wording" and omitted the idempotency prefixes. Every string below has since
/// been read out of <c>locusAPI.cls</c> line by line and corrected. Do not re-derive them
/// by analogy — the ABL is not internally consistent (see the two divergences below).
/// </para>
/// <para>
/// <b>Divergence 1 — the not-found path is not uniform.</b> <c>reject</c> (1832),
/// <c>holdreleasereject</c> (2002) and <c>cancelreject</c> (2124) call
/// <c>holdOrderJob</c> when the carton is missing. <c>holdreject</c> does <b>not</b>: the
/// call is commented out at <c>locusAPI.cls:1912</c> with the note
/// <i>"Removed to prevent hold loops"</i>. Holding a job because a hold was rejected asks
/// Locus to hold it again, which fails, which holds it again. Preserved via
/// <see cref="HoldsJobWhenCartonMissing"/>.
/// </para>
/// <para>
/// <b>Divergence 2 — the alert email body is not always the audit comment.</b> In three
/// handlers <c>cBody</c> and <c>transactions.comments</c> are the same string. In
/// <c>cancelreject</c> they differ: the audit row reads
/// <c>"Cancel order rejected by Locus: "</c> (2150) and the email reads
/// <c>"Cnacel rejected by Locus: "</c> (2162) — a typo in the ABL. Preserved via
/// <see cref="BuildEmailBody"/>. Human-visible only; not on the wire to Locus.
/// </para>
/// </remarks>
public abstract class LocusRejectFamilyHandlerBase : LocusEventHandlerBase
{
    private readonly ICartonRepository _cartons;
    private readonly IAuditService _audit;
    private readonly ILocusClient _locus;
    private readonly INotificationService _notifications;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly LockOrder _lockOrder;

    protected LocusRejectFamilyHandlerBase(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        INotificationService notifications,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger logger,
        string operation,
        string ablSource)
        : base(logger)
    {
        _cartons = cartons ?? throw new ArgumentNullException(nameof(cartons));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _lockOrder = LockOrder.Declare(operation, ablSource, "cartonmst", "transactions");
    }

    /// <summary>The <c>JE</c> audit row's <c>comments</c> column. Includes <c>EventInfo</c>.</summary>
    protected abstract string BuildAuditComments(string eventInfo);

    /// <summary>The alert email's subject line.</summary>
    protected abstract string BuildEmailSubject(Carton carton);

    /// <summary>
    /// The alert email's body (ABL <c>cBody</c>). Identical to the audit <c>comments</c> in
    /// three of the four handlers, so that is the default; <c>cancelreject</c> overrides it.
    /// See divergence 2 in the class remarks.
    /// </summary>
    protected virtual string BuildEmailBody(string eventInfo) => BuildAuditComments(eventInfo);

    /// <summary>
    /// Whether to call <c>holdOrderJob</c> when the carton cannot be found.
    /// <see langword="true"/> for three of the four handlers; <c>holdreject</c> overrides it
    /// to <see langword="false"/>. See divergence 1 in the class remarks.
    /// </summary>
    protected virtual bool HoldsJobWhenCartonMissing => true;

    /// <summary>The 200 response body on success.</summary>
    protected abstract string BuildSuccessResponse(string jobId);

    /// <summary>The 200 response body when the carton is already <c>C</c> or <c>E</c>.</summary>
    protected abstract string BuildIdempotentResponse(string jobId);

    public sealed override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        // ABL parses EventInfo, JobId, JobStatus, JobDate. JobStatus/JobDate are unused,
        // same as the ACCEPT handler.
        var jobId = ReadString(root, "JobId") ?? string.Empty;
        var eventInfo = ReadString(root, "EventInfo") ?? string.Empty;

        var carton = await _cartons
            .FindByCartonIdAsync(_context.Company, _context.Warehouse, jobId, cancellationToken)
            .ConfigureAwait(false);

        if (carton is null)
        {
            var notFoundBody = $"Unable to find carton matching job id {jobId}";

            // Not uniform across the family — see divergence 1 in the class remarks.
            // holdreject deliberately does not hold the job (locusAPI.cls:1912).
            if (HoldsJobWhenCartonMissing)
            {
                await _locus.HoldOrderJobAsync(jobId, notFoundBody, cancellationToken).ConfigureAwait(false);
            }

            Logger.LogWarning("Locus {EventType}: {Body}", EventType, notFoundBody);
            return InboundEventResult.ApplicationFailure(notFoundBody);
        }

        // Idempotency guard. ABL: if lookup(cartonmst.row_status,"C,E":U) gt 0.
        //
        // ⚠ CASE-INSENSITIVE on purpose. ABL's LOOKUP ignores case, and so does every
        // character comparison in this database — only 1 of 2,431 fields in irms.df is
        // declared CASE-SENSITIVE. The codebase writes both cases of the same status
        // (pick_status has INITIAL "O" and is queried as "o" in six places), so a
        // case-sensitive check here would re-process a carton the ABL considers finished:
        // a second audit row, a second alert email, and row_status flipped from 'c' to 'E'.
        if (string.Equals(carton.RowStatus, "C", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(carton.RowStatus, "E", StringComparison.OrdinalIgnoreCase))
        {
            return InboundEventResult.Ok(BuildIdempotentResponse(jobId));
        }

        var comments = BuildAuditComments(eventInfo);

        await _unitOfWork.BeginAsync(_lockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            // cartonmst first — see the lock-order note in the class remarks.
            await _cartons.MarkErrorUnlessCompleteAsync(
                carton.Company, carton.Warehouse, carton.CartonId, cancellationToken)
                .ConfigureAwait(false);

            await _audit.AppendAsync(
                new AuditTransaction
                {
                    Company = carton.Company,
                    Warehouse = carton.Warehouse,
                    TransactionType = "JE",
                    EmployeeNumber = _context.UserId,
                    CartonId = carton.CartonId,
                    OrderNumber = carton.Order,
                    OrderSuffix = carton.OrderSuffix,
                    Comments = comments,
                },
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // Email is a platform concern, not a handler one (docs/HANDLER_SPECS.md Group 2).
        // The stub logs and returns a failed OperationResult rather than throwing, so an
        // alert failure never takes down the response to Locus — same treatment as the
        // HoldOrderJobAsync call above.
        var subject = BuildEmailSubject(carton);
        var body = BuildEmailBody(eventInfo);
        await _notifications.SendAlertAsync(subject, body, cancellationToken).ConfigureAwait(false);

        return InboundEventResult.Ok(BuildSuccessResponse(jobId));
    }
}

/// <summary>REJECT — Locus rejects an order job. Converts <c>locusAPI.cls:1803-1877</c>.</summary>
/// <remarks>The template for the whole family — see <see cref="LocusRejectFamilyHandlerBase"/>.</remarks>
public sealed class LocusRejectHandler : LocusRejectFamilyHandlerBase
{
    public LocusRejectHandler(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        INotificationService notifications,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusRejectHandler> logger)
        : base(
            cartons, audit, locus, notifications, unitOfWork, context, logger,
            operation: "locus reject",
            ablSource: "api/wms/locusAPI.cls:1803-1877 (do transaction)")
    {
    }

    public override string EventType => LocusEventType.Reject;

    protected override string BuildAuditComments(string eventInfo) =>
        $"OrderJobResult rejected by Locus: {eventInfo}";

    protected override string BuildEmailSubject(Carton carton) =>
        $"Error sending carton {carton.Order} - {carton.CartonId} to locus";

    protected override string BuildSuccessResponse(string jobId) =>
        $"OrderJobResult reject successful for job id {jobId}";

    // Verified: locusAPI.cls:1840.
    protected override string BuildIdempotentResponse(string jobId) =>
        $"OrderJobResult reject already processed for job id {jobId}";
}

/// <summary>HOLDREJECT. Converts <c>locusAPI.cls:1884-1957</c>.</summary>
/// <remarks>
/// All four strings verified against the ABL. Note this handler is the family's odd one
/// out on the not-found path: it does <b>not</b> call <c>holdOrderJob</c>
/// (<c>locusAPI.cls:1912</c>, commented out — "Removed to prevent hold loops"). See
/// divergence 1 in <see cref="LocusRejectFamilyHandlerBase"/>.
/// </remarks>
public sealed class LocusHoldRejectHandler : LocusRejectFamilyHandlerBase
{
    public LocusHoldRejectHandler(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        INotificationService notifications,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusHoldRejectHandler> logger)
        : base(
            cartons, audit, locus, notifications, unitOfWork, context, logger,
            operation: "locus holdreject",
            ablSource: "api/wms/locusAPI.cls:1884-1957 (do transaction)")
    {
    }

    public override string EventType => LocusEventType.HoldReject;

    // ⚠ locusAPI.cls:1912 — the holdOrderJob call is commented out here and only here.
    protected override bool HoldsJobWhenCartonMissing => false;

    // Verified: locusAPI.cls:1938. Note "Hold rejected", not "OrderJobResult hold
    // rejected" — this handler's comment does not carry the "OrderJobResult" prefix that
    // reject's does.
    protected override string BuildAuditComments(string eventInfo) =>
        $"Hold rejected by Locus: {eventInfo}";

    // Verified: locusAPI.cls:1949. "putting ... on hold", not "holding".
    protected override string BuildEmailSubject(Carton carton) =>
        $"Error putting carton {carton.Order} - {carton.CartonId} on hold in locus";

    // Verified: locusAPI.cls:1954.
    protected override string BuildSuccessResponse(string jobId) =>
        $"OrderJobResult hold reject successful for job id {jobId}";

    // Verified: locusAPI.cls:1920.
    protected override string BuildIdempotentResponse(string jobId) =>
        $"OrderJobResult hold reject already processed for job id {jobId}";
}

/// <summary>
/// HOLDRELEASEREJECT. Converts <c>locusAPI.cls:1974-2047</c>.
/// </summary>
/// <remarks>
/// ⚠ ABL DEFECT (<c>docs/HANDLER_SPECS.md</c> Group 2, the most consequential of the four
/// string defects). Line 2045 returns <c>holdcomplete</c>'s success message —
/// <c>"OrderJobResult hold complete successful for job id " + JobId</c> — instead of a
/// hold-release-reject message, reporting <b>success</b> for what is actually a
/// <b>rejection</b>. Reproduced exactly. Anything downstream parsing these strings sees a
/// hold-release rejection as a hold completion. Flag for the captured-traffic review
/// before ever "fixing" this — the audit row and email subject are unaffected and use the
/// correct hold-release-reject wording, both given exactly by the spec.
/// </remarks>
public sealed class LocusHoldReleaseRejectHandler : LocusRejectFamilyHandlerBase
{
    public LocusHoldReleaseRejectHandler(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        INotificationService notifications,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusHoldReleaseRejectHandler> logger)
        : base(
            cartons, audit, locus, notifications, unitOfWork, context, logger,
            operation: "locus holdreleasereject",
            ablSource: "api/wms/locusAPI.cls:1974-2047 (do transaction)")
    {
    }

    public override string EventType => LocusEventType.HoldReleaseReject;

    // Verified: locusAPI.cls:2028.
    protected override string BuildAuditComments(string eventInfo) =>
        $"Hold release rejected by Locus: {eventInfo}";

    // Verified: locusAPI.cls:2039.
    protected override string BuildEmailSubject(Carton carton) =>
        $"Error releasing carton {carton.Order} - {carton.CartonId} from hold in locus";

    protected override string BuildSuccessResponse(string jobId) =>
        // ⚠ ABL DEFECT: locusAPI.cls:2045 — copy-paste bug, reproduced exactly. See the
        // class remarks.
        $"OrderJobResult hold complete successful for job id {jobId}";

    // Verified: locusAPI.cls:2010. The idempotency guard uses the correct
    // hold-release-reject wording — it is a separate, earlier code path and is not
    // affected by the line-2045 copy-paste bug.
    protected override string BuildIdempotentResponse(string jobId) =>
        $"OrderJobResult hold release reject already processed for job id {jobId}";
}

/// <summary>CANCELREJECT. Converts <c>locusAPI.cls:2096-2169</c>.</summary>
/// <remarks>
/// All strings verified against the ABL. Three things here defeat analogy, which is why
/// the earlier paraphrased spec got two of them wrong:
/// <list type="number">
/// <item>the success response reads "cancel <i>order</i> successful", with no "reject" in
/// it at all, while the idempotency response reads "cancel <i>reject</i> already
/// processed" — the two use different nouns for the same event;</item>
/// <item>the audit comment reads "Cancel order rejected", not "OrderJobResult cancel
/// rejected";</item>
/// <item>⚠ the alert email body reads <c>"Cnacel rejected by Locus: "</c> — a typo in the
/// ABL at line 2162, and the only place in the family where the email body differs from
/// the audit comment. Reproduced; see divergence 2 in
/// <see cref="LocusRejectFamilyHandlerBase"/>.</item>
/// </list>
/// </remarks>
public sealed class LocusCancelRejectHandler : LocusRejectFamilyHandlerBase
{
    public LocusCancelRejectHandler(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        INotificationService notifications,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusCancelRejectHandler> logger)
        : base(
            cartons, audit, locus, notifications, unitOfWork, context, logger,
            operation: "locus cancelreject",
            ablSource: "api/wms/locusAPI.cls:2096-2169 (do transaction)")
    {
    }

    public override string EventType => LocusEventType.CancelReject;

    // Verified: locusAPI.cls:2150.
    protected override string BuildAuditComments(string eventInfo) =>
        $"Cancel order rejected by Locus: {eventInfo}";

    // Verified: locusAPI.cls:2161.
    protected override string BuildEmailSubject(Carton carton) =>
        $"Error cancelling carton {carton.Order} - {carton.CartonId} in locus";

    // ⚠ ABL DEFECT (typo): locusAPI.cls:2162 reads "Cnacel", not "Cancel", and so is the
    // one place in the family where the email body is not the audit comment. Reproduced
    // exactly — it appears in operators' alert mailboxes and may well be what they search
    // for. Fixing it is a one-line change once someone confirms no mail rule matches it.
    protected override string BuildEmailBody(string eventInfo) =>
        $"Cnacel rejected by Locus: {eventInfo}";

    // Verified: locusAPI.cls:2166.
    protected override string BuildSuccessResponse(string jobId) =>
        $"OrderJobResult cancel order successful for job id {jobId}";

    // Verified: locusAPI.cls:2132. Note "cancel reject", not "cancel order" — it does not
    // match the success string's phrasing.
    protected override string BuildIdempotentResponse(string jobId) =>
        $"OrderJobResult cancel reject already processed for job id {jobId}";
}
