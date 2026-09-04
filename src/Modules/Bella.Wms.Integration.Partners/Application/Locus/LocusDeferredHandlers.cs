using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// PICK — the hardest single item in the <c>api/</c> module.
/// </summary>
/// <remarks>
/// <para>
/// Converts <c>locusAPI.cls:2544-3076</c> (532 lines). The ABL:
/// </para>
/// <list type="number">
///   <item>Opens a <c>PICK-BLOCK</c> transaction.</item>
///   <item>Optionally calls <c>rf/wsset_cyc.p</c> to set a cycle count (line 2933).</item>
///   <item>Runs <c>wms_webhost/wspick.p</c> persistently <b>inside</b> that transaction (line 2954).</item>
///   <item>Calls <c>PickData</c> with <c>string(rowid(pick))</c> — a physical database address (line 2956).</item>
///   <item>Calls <c>DoPick</c>, which takes no inputs and reads state from the persistent instance (line 2977).</item>
///   <item>Re-finds <c>transactions</c> rows <c>EXCLUSIVE-LOCK</c> and overwrites <c>comment</c> (line 3013).</item>
///   <item>On failure, writes a <c>JE</c> audit row and calls <c>holdOrderJob</c>.</item>
///   <item>In <c>FINALLY</c>, runs <c>destroy in hproc</c> to free <c>wspick.p</c>'s memory.</item>
/// </list>
/// <para>
/// Three separate blockers, any one of which is sufficient to defer:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>The ROWID handoff.</b> A ROWID is a physical address with no meaning outside
///     the ABL session that read it. It cannot cross an HTTP or RPC seam, so no callback
///     bridge preserves this call shape unchanged.
///   </item>
///   <item>
///     <b>The transaction spans the module boundary.</b> Phase 6 §4 rule 1 forbids it,
///     and rule 3 forbids holding locks across the I/O this path performs.
///   </item>
///   <item>
///     <b><c>wspick.p</c> is 5,890 lines of picking engine</b> and belongs to
///     B07 Picking, which is not in scope for this module.
///   </item>
/// </list>
/// </remarks>
public sealed class LocusPickHandler : DeferredLocusEventHandler
{
    public LocusPickHandler(ILogger<LocusPickHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.Pick;

    protected override string AblSource => "api/wms/locusAPI.cls:2544-3076 (pick)";

    protected override string WarehouseCoreDependency =>
        "wms_webhost/wspick.p (5,890 lines) via PickData/DoPick, plus rf/wsset_cyc.p";
}

/// <summary>PICKCOMPLETE. Converts <c>locusAPI.cls:3078-3186</c>.</summary>
public sealed class LocusPickCompleteHandler : DeferredLocusEventHandler
{
    public LocusPickCompleteHandler(ILogger<LocusPickCompleteHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.PickComplete;

    protected override string AblSource => "api/wms/locusAPI.cls:3078-3186 (pickcomplete)";

    protected override string WarehouseCoreDependency =>
        "wms_webhost/wspick.p, reached through the same persistent handle as PICK";
}

/// <summary>PUTAWAYREQUEST. Converts <c>locusAPI.cls:3210-3241</c>.</summary>
/// <remarks>
/// <para>
/// Branches on the same first-character rule as the router
/// (<see cref="LocusLicencePlateRule"/>, <c>docs/HANDLER_SPECS.md</c> Group 5): a
/// <c>LicensePlate</c> whose first character parses as an integer is an SSCC-18 and calls
/// <see cref="ILocusClient.CreatePutawayJobAsync"/> (ABL <c>createPutawayJob</c>);
/// anything else is a tote and calls
/// <see cref="ILocusClient.CreatePutawayJobForToteAsync"/> (ABL
/// <c>locusCreatePutawayJob</c>).
/// </para>
/// <para>
/// Both are outbound Locus calls on <see cref="ILocusClient"/>, currently
/// <c>NotImplementedLocusClient</c> until Slice 3. This handler's response does not
/// depend on the outbound call succeeding, so it is written and returns correctly now —
/// the stub logs an error rather than throwing.
/// </para>
/// <para>
/// <c>RequestDate</c> and <c>RequestUser</c> are parsed by the ABL per the spec, but
/// neither the branch decision, the outbound call, nor the response text uses them, so
/// nothing here reads them either.
/// </para>
/// </remarks>
public sealed class LocusPutawayRequestHandler : LocusEventHandlerBase
{
    private readonly ILocusClient _locus;

    public LocusPutawayRequestHandler(ILocusClient locus, ILogger<LocusPutawayRequestHandler> logger)
        : base(logger)
    {
        _locus = locus ?? throw new ArgumentNullException(nameof(locus));
    }

    public override string EventType => LocusEventType.PutawayRequest;

    public override async Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(request.Payload);
        var root = document.RootElement;

        var licensePlate = ReadString(root, "LicensePlate") ?? string.Empty;
        var robot = ReadString(root, "RequestRobot") ?? string.Empty;

        if (LocusLicencePlateRule.FirstCharacterIsNumeric(licensePlate))
        {
            await _locus.CreatePutawayJobAsync(licensePlate, robot, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _locus.CreatePutawayJobForToteAsync(licensePlate, robot, cancellationToken)
                .ConfigureAwait(false);
        }

        // Says "SSCC18" even on the tote branch, and ends with a trailing space —
        // reproduced exactly per docs/HANDLER_SPECS.md Group 5.
        return InboundEventResult.Ok($"PutawayJobRequest for SSCC18 {licensePlate} ");
    }
}

/// <summary>PUTAWAYACCEPT. Converts <c>locusAPI.cls:4495-4502</c>.</summary>
/// <remarks>Four lines in the ABL — no job id, no payload parsing (<c>docs/HANDLER_SPECS.md</c> Group 5).</remarks>
public sealed class LocusPutawayAcceptHandler : LocusEventHandlerBase
{
    public LocusPutawayAcceptHandler(ILogger<LocusPutawayAcceptHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.PutawayAccept;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(InboundEventResult.Ok("PutawayJobResult accept successful"));
}

/// <summary>PUTAWAYREJECT. Converts <c>locusAPI.cls:3895-4095</c>.</summary>
public sealed class LocusPutawayRejectHandler : LocusEventHandlerBase
{
    public LocusPutawayRejectHandler(ILogger<LocusPutawayRejectHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.PutawayReject;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Converts api/wms/locusAPI.cls:3895-4095.");
}

/// <summary>
/// PUTAWAYPUTINDUCT. Converts <c>locusAPI.cls:4503-4509</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four lines.</b> It sets a 200 and a fixed message. No payload parsing, no database,
/// no outbound call — it exists so Locus gets an acknowledgement.
/// </para>
/// <para>
/// This sat stubbed because <c>docs/HANDLER_SPECS.md</c> said "the extraction could not
/// cleanly bound this method — read it directly before starting", and nobody did. Reading
/// it took a minute. Third documentation claim this project has found to be wrong about
/// its own code; the others were <c>trigger/AGENTS.md</c> describing triggers that are
/// empty stubs, and the <c>toteinduct</c> stub asserting a call to <c>rf/wsset_cyc.p</c>
/// that is 400 lines away in a different method.
/// </para>
/// <para>
/// It is a near-twin of <c>putawayaccept</c> (<c>4495-4501</c>), which sits directly
/// above it and is equally trivial.
/// </para>
/// </remarks>
public sealed class LocusPutawayPutInductHandler : LocusEventHandlerBase
{
    public LocusPutawayPutInductHandler(ILogger<LocusPutawayPutInductHandler> logger) : base(logger) { }

    public override string EventType => LocusEventType.PutawayPutInduct;

    public override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request, CancellationToken cancellationToken = default) =>
        // Verified: locusAPI.cls:4506. No job id appended — like putawayaccept, and
        // unlike every OrderJobResult response.
        Task.FromResult(InboundEventResult.Ok("PutawayJobResult put induct successful"));
}
