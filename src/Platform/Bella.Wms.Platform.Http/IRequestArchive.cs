using Bella.Wms.Platform.Context;

namespace Bella.Wms.Platform.Http;

/// <summary>
/// Archives inbound and outbound payloads, preserving a behaviour the operations team
/// relies on.
/// </summary>
/// <remarks>
/// <para>
/// Every ABL router writes the request and the response to disk before and after
/// dispatch. <c>wsLocusAPI.p:167</c> writes to
/// <c>{locusPath}/inbound/requests/{action}_{jobId}_{mtime}.json</c> and line 187 writes
/// the response alongside it; <c>wsMiddlewareAPI.p:331</c> does the same under
/// <c>{rule 2013}/middleware/{company}/{warehouse}/inbound/</c>.
/// </para>
/// <para>
/// This is not incidental logging. When Locus or the AutoBagger middleware disputes what
/// was sent, these files are the evidence, and warehouse staff open them directly. The
/// conversion keeps the capability and the on-disk layout so existing runbooks and
/// support habits still work.
/// </para>
/// <para>
/// <b>Redaction is new.</b> The ABL wrote payloads verbatim, so any credential in a
/// header or body landed on disk in clear text. Implementations here redact before
/// writing.
/// </para>
/// </remarks>
public interface IRequestArchive
{
    Task RecordRequestAsync(
        WmsHttpRequest request,
        IWmsContext context,
        CancellationToken cancellationToken = default);

    Task RecordResponseAsync(
        WmsHttpRequest request,
        WmsHttpResponse<string> response,
        IWmsContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives an inbound request received by this stack, mirroring the ABL routers'
    /// pre-dispatch write.
    /// </summary>
    /// <param name="channel">Logical channel — <c>locus</c>, <c>middleware</c>, <c>pyramid</c>.</param>
    /// <param name="action">The resolved event/action name used in the filename.</param>
    /// <param name="correlationKey">Job id, licence plate or carton id, as the ABL used.</param>
    /// <param name="payload">The raw inbound payload.</param>
    /// <param name="context">Ambient context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordInboundAsync(
        string channel,
        string action,
        string correlationKey,
        string payload,
        IWmsContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Archives the response this stack returned for an inbound request.</summary>
    Task RecordInboundResponseAsync(
        string channel,
        string action,
        string correlationKey,
        string payload,
        IWmsContext context,
        CancellationToken cancellationToken = default);
}
