using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Partners.Contracts;

/// <summary>
/// One inbound event from an external partner system.
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface exists to delete <c>dynamic-invoke</c>.</b> All four ABL routers
/// dispatch by reflection on a string taken straight from the request payload:
/// </para>
/// <code>
/// dynamic-invoke(mylocus, cAction, input jsoOrderJobResult,
///                output sHttpStatus, output lcResponse) no-error.
/// </code>
/// <para>
/// (<c>wsLocusAPI.p:175,219,283</c>; <c>wsMiddlewareAPI.p:351</c>;
/// <c>wsPyramidAPI.p:153</c>; <c>accutechAutoBaggerOut.cls:123</c>.)
/// </para>
/// <para>
/// There is no route table in the ABL. The effective route table is every public method
/// on the target class — 41 of them on <c>locusAPI.cls</c> — which means an event name
/// arriving from outside can reach methods that were never meant to be endpoints, such
/// as <c>parseJSOProperty</c> or <c>releaseReplenishment</c>. Making each handler an
/// explicit registration closes that, and gives the codebase a list of what the API
/// actually accepts, which it does not currently have anywhere except comment blocks.
/// </para>
/// </remarks>
public interface IInboundEventHandler
{
    /// <summary>
    /// The channel this handler serves — <c>locus</c>, <c>middleware</c>, <c>pyramid</c>.
    /// </summary>
    string Channel { get; }

    /// <summary>
    /// The event name as it arrives on the wire, matched case-insensitively.
    /// The ABL lower-cases method names implicitly, so <c>PICKCOMPLETE</c> and
    /// <c>pickcomplete</c> both resolve today; that leniency is preserved.
    /// </summary>
    string EventType { get; }

    Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>An inbound event.</summary>
/// <param name="Channel">Originating channel.</param>
/// <param name="EventType">Resolved event name.</param>
/// <param name="Payload">The raw JSON body the partner sent.</param>
/// <param name="CorrelationKey">Job id, licence plate or carton id — used in the archive filename.</param>
public readonly record struct InboundEventRequest(
    string Channel,
    string EventType,
    string Payload,
    string CorrelationKey);

/// <summary>
/// The result of handling an inbound event.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the ABL <c>output sHttpStatus as character, output lcResponse as longchar</c>
/// pair exactly, including the fact that <c>sHttpStatus</c> is a string the handler sets
/// itself rather than something derived from success.
/// </para>
/// <para>
/// <b>Preserved oddity.</b> The ABL handlers return HTTP <c>200</c> for application-level
/// failures, setting <c>lcResponse</c> to an error string
/// (<c>locusAPI.cls:2968-2972</c>, and the <c>Invalid Endpoint Action</c> path at
/// <c>wsLocusAPI.p:178-181</c>). Locus and the AutoBagger middleware have been running
/// against that for years and their retry behaviour may depend on it. Do not "fix" this
/// to a 4xx without evidence from captured traffic.
/// </para>
/// </remarks>
public readonly record struct InboundEventResult
{
    private InboundEventResult(int statusCode, string body, bool handlerSucceeded)
    {
        StatusCode = statusCode;
        Body = body;
        HandlerSucceeded = handlerSucceeded;
    }

    /// <summary>The status to return to the partner. ABL <c>sHttpStatus</c>.</summary>
    public int StatusCode { get; }

    /// <summary>The response body. ABL <c>lcResponse</c>.</summary>
    public string Body { get; }

    /// <summary>
    /// Whether the handler actually succeeded — which is <i>not</i> the same as
    /// <see cref="StatusCode"/> being 200. Used for logging and metrics, never for the
    /// wire response.
    /// </summary>
    public bool HandlerSucceeded { get; }

    public static InboundEventResult Ok(string body) => new(200, body, true);

    /// <summary>
    /// An application-level failure reported as HTTP 200, matching ABL behaviour.
    /// </summary>
    public static InboundEventResult ApplicationFailure(string body) => new(200, body, false);

    /// <summary>A genuine protocol-level rejection — bad auth, unparseable body.</summary>
    public static InboundEventResult Rejected(int statusCode, string body) =>
        new(statusCode, body, false);
}
