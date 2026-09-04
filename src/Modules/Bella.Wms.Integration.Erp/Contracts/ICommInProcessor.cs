using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Erp.Contracts;

/// <summary>
/// Contract every inbound (OMS → WMS) processor implements.
/// </summary>
/// <remarks>
/// Translation of <c>api/wms/interface/IWMSCommInProcessor.cls</c>, with the same
/// registration-by-property change as <see cref="ICommOutProcessor"/>. The concrete
/// inbound processor today is <c>oms_ircodn.cls</c> (1,523 lines), which handles order
/// download and carries the <c>changenc.i</c> change-tracking behaviour.
/// </remarks>
public interface ICommInProcessor
{
    /// <summary>The <c>event_type</c> this processor handles.</summary>
    string EventType { get; }

    /// <summary>The <c>comm_endpoint</c> this processor serves.</summary>
    string Endpoint { get; }

    Task<OperationResult<CommInResult>> ProcessAsync(
        CommInRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input to <see cref="ICommInProcessor.ProcessAsync"/>.</summary>
/// <param name="Payload">The inbound payload from the host system.</param>
/// <param name="CommId">The <c>comm_in.comm_id</c> being processed.</param>
/// <param name="TransmissionId">The transmission this message belongs to.</param>
public readonly record struct CommInRequest(
    string Payload,
    long CommId,
    string TransmissionId);

/// <summary>
/// Output of <see cref="ICommInProcessor.ProcessAsync"/>. Mirrors
/// <c>api/wms/interface/comm_in_result.cls</c>.
/// </summary>
/// <param name="Response">Response payload to record against the message.</param>
/// <param name="StatusCode">Application status code.</param>
/// <param name="RecordsAffected">
/// How many WMS records the message changed. The ABL tracks this for the interface log.
/// </param>
public readonly record struct CommInResult(
    string Response,
    int StatusCode,
    int RecordsAffected);
