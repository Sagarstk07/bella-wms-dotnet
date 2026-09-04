using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>
/// Sends prepared payloads to the FDM4 ERP.
/// </summary>
/// <remarks>
/// Conversion of <c>api/wms/interface/tofdm4.cls</c> (510 lines).
/// </remarks>
public interface IFdm4Sender
{
    /// <summary>
    /// Sends one payload and returns what FDM4 replied.
    /// </summary>
    /// <param name="payload">The prepared JSON payload.</param>
    /// <param name="requestIdentifier">ABL <c>pRequestIdentifier</c>, e.g. <c>"wms_tofdm4"</c>.</param>
    /// <param name="fileName">Logical name used for the request archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<Fdm4Response>> SendAsync(
        string payload,
        string requestIdentifier,
        string fileName,
        CancellationToken cancellationToken = default);
}

/// <summary>What FDM4 returned.</summary>
/// <param name="StatusCode">HTTP status.</param>
/// <param name="Body">Raw response body.</param>
/// <param name="TransmissionId">The transmission id FDM4 echoed back, if present.</param>
public readonly record struct Fdm4Response(int StatusCode, string Body, string? TransmissionId);
