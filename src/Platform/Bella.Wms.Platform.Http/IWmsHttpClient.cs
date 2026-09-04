using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Platform.Http;

/// <summary>
/// The single outbound HTTP abstraction for every external integration.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces.</b> Roughly 1,900 lines of ABL and shell:
/// </para>
/// <list type="table">
///   <listheader><term>ABL / shell</term><description>Lines</description></listheader>
///   <item><term><c>api/wms/httpRequestCURL.cls</c></term><description>529</description></item>
///   <item><term><c>api/wms/apiHelpers.i</c></term><description>642</description></item>
///   <item><term><c>api/wms/jsonHelpers.i</c></term><description>269</description></item>
///   <item><term><c>api/wms/headerCurl.sh</c></term><description>65</description></item>
///   <item><term><c>api/wms/locusCurl.sh</c></term><description>40</description></item>
///   <item><term><c>api/wms/pyramidCurl.sh</c></term><description>26</description></item>
///   <item><term><c>api/wms/locusTestCurl.sh</c></term><description>21</description></item>
///   <item><term><c>api/wms/globaleCurl.sh</c></term><description>18</description></item>
/// </list>
/// <para>
/// The ABL pattern is: serialise the payload to a temp file, build a shell command,
/// <c>OS-COMMAND</c> out to cURL, poll for a response file, read it back, delete the
/// temp files. Nine <c>OS-COMMAND</c>/<c>OS-DELETE</c> sites in
/// <c>httpRequestCURL.cls</c> alone. All of that collapses into <c>HttpClient</c>.
/// </para>
/// <para>
/// <b>Three behavioural differences, all deliberate, all recorded.</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>TLS verification is on.</b> Every ABL cURL invocation passes <c>--insecure</c>
///     (<c>headerCurl.sh</c> lines 38-46, <c>globaleCurl.sh:15</c>). <c>HttpClient</c>
///     validates by default and this code does not disable it. Endpoints with an
///     incomplete chain will start failing — find them during the slice, not at cutover.
///   </item>
///   <item>
///     <b>No shell.</b> <c>headerCurl.sh</c> builds a command string and runs
///     <c>eval $CMD</c> with interpolated URI, headers and body. That is a shell
///     injection surface wherever any of those derive from data. There is no shell here.
///   </item>
///   <item>
///     <b>Fire-and-forget is explicit.</b> ABL's <c>runAsBackGroundTaskNextTime</c>
///     (<c>httpRequestCURL.cls:58</c>) appends <c>&amp;</c> to the shell command, which
///     abandons the result and skips file cleanup. Work that should outlive the request
///     goes on the queue instead — see <c>Bella.Wms.Jobs</c>.
///   </item>
/// </list>
/// </remarks>
public interface IWmsHttpClient
{
    /// <summary>
    /// Sends a request and deserialises a JSON response body.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="TResponse">Response DTO type.</typeparam>
    Task<OperationResult<WmsHttpResponse<TResponse>>> SendAsync<TResponse>(
        WmsHttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and returns the raw response body, for endpoints whose response
    /// shape is not yet modelled.
    /// </summary>
    Task<OperationResult<WmsHttpResponse<string>>> SendRawAsync(
        WmsHttpRequest request,
        CancellationToken cancellationToken = default);
}
