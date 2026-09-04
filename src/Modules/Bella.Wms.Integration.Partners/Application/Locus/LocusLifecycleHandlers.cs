using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// ACCEPT — Locus acknowledges an order job.
/// </summary>
/// <remarks>
/// <para>
/// <b>Worked reference implementation.</b> Converts <c>locusAPI.cls:1756-1801</c> in
/// full. Everything the ABL does is here: parse three properties, look up the carton,
/// write a <c>JA</c> audit row, return 200. Nothing in this path touches picking or
/// shipping, which is why it converts cleanly and why the lifecycle events are Slice 4.
/// </para>
/// <para>
/// Use this as the pattern for the other lifecycle handlers.
/// </para>
/// </remarks>
public sealed class LocusAcceptHandler : LocusEventHandlerBase
{
    private static readonly LockOrder AuditLockOrder = LockOrder.Declare(
        operation: "locus accept audit",
        ablSource: "api/wms/locusAPI.cls:1782-1796 (do transaction)",
        "transactions");

    private readonly ICartonRepository _cartons;
    private readonly IAuditService _audit;
    private readonly ILocusClient _locus;
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;

    public LocusAcceptHandler(
        ICartonRepository cartons,
        IAuditService audit,
        ILocusClient locus,
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        ILogger<LocusAcceptHandler> logger)
        : base(logger)
    {
        _cartons = cartons ?? throw new ArgumentNullException(nameof(cartons));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override string EventType => LocusEventType.Accept;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        // ABL lines 1761-1765. JobStatus and JobDate are parsed but only JobId is used;
        // JobDate is explicitly left unwritten with the comment "need to format to
        // format in n_trans.t". Not carrying that forward as a silent omission — see
        // the note on the audit write below.
        var jobId = ReadString(root, "JobId") ?? string.Empty;

        // ABL lines 1767-1772: find first cartonmst ... NO-LOCK.
        var carton = await _cartons
            .FindByCartonIdAsync(_context.Company, _context.Warehouse, jobId, cancellationToken)
            .ConfigureAwait(false);

        if (carton is null)
        {
            // ABL lines 1774-1780: HTTP 200 with an error body, and the job is put on
            // hold at Locus. The 200 is deliberate — see InboundEventResult remarks.
            var body = $"Unable to find carton matching job id {jobId}";

            await _locus.HoldOrderJobAsync(jobId, body, cancellationToken).ConfigureAwait(false);

            Logger.LogWarning("Locus ACCEPT: {Body}", body);
            return InboundEventResult.ApplicationFailure(body);
        }

        await _unitOfWork.BeginAsync(AuditLockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            // ABL lines 1783-1795. Trans type "JA" and the literal comment text are
            // wire-visible to anyone reading the audit trail, so both are preserved
            // exactly.
            await _audit.AppendAsync(
                new AuditTransaction
                {
                    Company = carton.Company,
                    Warehouse = carton.Warehouse,
                    TransactionType = "JA",
                    EmployeeNumber = _context.UserId,
                    CartonId = carton.CartonId,
                    OrderNumber = carton.Order,
                    OrderSuffix = carton.OrderSuffix,
                    Comments = "OrderJobResult accepted by Locus",

                    // The ABL leaves date_time unset here (line 1793 is commented out,
                    // "need to format to format in n_trans.t"), so the n_trans.t CREATE
                    // trigger supplies it. Since that trigger is being replaced by
                    // IAuditService, the value must now come from somewhere explicit —
                    // AuditService.Enrich supplies DateTimeOffset.Now, which is what the
                    // trigger did. Recorded in docs/ABL_CROSSREF.md.
                },
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        // ABL lines 1798-1799.
        return InboundEventResult.Ok($"OrderJobResult accept successful for job id {jobId}");
    }
}

/// <summary>HOLDCOMPLETE. Converts <c>locusAPI.cls:1878-1883</c>.</summary>
/// <remarks>
/// Four lines in the ABL — it does not even parse the payload.
/// <para>
/// ⚠ ABL DEFECT (<c>docs/HANDLER_SPECS.md</c> Group 1): the response is a fixed string
/// with a trailing space and no job id appended. Reproduced exactly — this is
/// wire-visible and Locus may be logging it. Do not "fix" without evidence from captured
/// traffic.
/// </para>
/// </remarks>
public sealed class LocusHoldCompleteHandler : LocusEventHandlerBase
{
    public LocusHoldCompleteHandler(ILogger<LocusHoldCompleteHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.HoldComplete;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InboundEventResult.Ok(
            // ⚠ ABL DEFECT: locusAPI.cls:1878-1883 never appends the job id — the ABL
            // does not parse the payload at all here. Trailing space is part of the
            // string.
            "OrderJobResult hold complete successful for job id "));
}

/// <summary>HOLDRELEASECOMPLETE. Converts <c>locusAPI.cls:1958-1973</c>.</summary>
/// <remarks>
/// The only one of the five Group 1 handlers that actually appends <c>JobId</c> to its
/// response — see <c>docs/HANDLER_SPECS.md</c> Group 1 for the other four, which parse it
/// and drop it.
/// </remarks>
public sealed class LocusHoldReleaseCompleteHandler : LocusEventHandlerBase
{
    public LocusHoldReleaseCompleteHandler(ILogger<LocusHoldReleaseCompleteHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.HoldReleaseComplete;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);

        // ABL locusAPI.cls:1958-1973. Unlike the other four Group 1 handlers, this one
        // does append the parsed JobId.
        var jobId = ReadString(document.RootElement, "JobId") ?? string.Empty;

        return Task.FromResult(InboundEventResult.Ok(
            $"OrderJobResult hold release complete successful for job id {jobId}"));
    }
}

/// <summary>UPDATECOMPLETE. Converts <c>locusAPI.cls:2048-2063</c>.</summary>
/// <remarks>
/// ⚠ ABL DEFECT (<c>docs/HANDLER_SPECS.md</c> Group 1): <c>JobId</c> is parsed into a
/// variable and never used. Reproduced exactly, including the unused parse and the
/// trailing space with no id in the response.
/// </remarks>
public sealed class LocusUpdateCompleteHandler : LocusEventHandlerBase
{
    public LocusUpdateCompleteHandler(ILogger<LocusUpdateCompleteHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.UpdateComplete;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);

        // ABL: cJobID = parseJSOProperty(jsoOrderJobResult,"JobId") — parsed, never used.
        // ⚠ ABL DEFECT: reproduced.
        _ = ReadString(document.RootElement, "JobId");

        return Task.FromResult(InboundEventResult.Ok(
            "OrderJobResult update complete successful for job id "));
    }
}

/// <summary>UPDATEREJECT. Converts <c>locusAPI.cls:2064-2079</c>.</summary>
/// <remarks>
/// ⚠ ABL DEFECT (<c>docs/HANDLER_SPECS.md</c> Group 1): <c>JobId</c> is parsed into a
/// variable and never used. Reproduced exactly, including the unused parse and the
/// trailing space with no id in the response.
/// </remarks>
public sealed class LocusUpdateRejectHandler : LocusEventHandlerBase
{
    public LocusUpdateRejectHandler(ILogger<LocusUpdateRejectHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.UpdateReject;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);

        // ABL: cJobID = parseJSOProperty(jsoOrderJobResult,"JobId") — parsed, never used.
        // ⚠ ABL DEFECT: reproduced.
        _ = ReadString(document.RootElement, "JobId");

        return Task.FromResult(InboundEventResult.Ok(
            "OrderJobResult update reject successful for job id "));
    }
}

/// <summary>CANCELCOMPLETE. Converts <c>locusAPI.cls:2080-2095</c>.</summary>
/// <remarks>
/// ⚠ ABL DEFECT (<c>docs/HANDLER_SPECS.md</c> Group 1): <c>JobId</c> is parsed into a
/// variable and never used. Reproduced exactly, including the unused parse and the
/// trailing space with no id in the response.
/// </remarks>
public sealed class LocusCancelCompleteHandler : LocusEventHandlerBase
{
    public LocusCancelCompleteHandler(ILogger<LocusCancelCompleteHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.CancelComplete;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);

        // ABL: cJobID = parseJSOProperty(jsoOrderJobResult,"JobId") — parsed, never used.
        // ⚠ ABL DEFECT: reproduced.
        _ = ReadString(document.RootElement, "JobId");

        return Task.FromResult(InboundEventResult.Ok(
            "OrderJobResult cancel complete successful for job id "));
    }
}
