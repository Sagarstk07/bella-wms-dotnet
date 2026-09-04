using Bella.Wms.Integration.Erp.Contracts;
using Bella.Wms.Integration.Erp.Domain;
using Bella.Wms.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>
/// Shared behaviour for outbound processors.
/// </summary>
/// <remarks>
/// <para>
/// Conversion of <c>api/wms/interface/WMSCommOutProcessorBase.cls</c> (204 lines), which
/// itself inherits <c>base.cls</c> (215 lines) for config loading, logging and cURL setup.
/// The cURL setup disappears — <see cref="IFdm4Sender"/> owns transport now — and the
/// config loading moves to <see cref="Fdm4Options"/>, so what is left here is the payload
/// lifecycle each processor shares.
/// </para>
/// <para>
/// The template method is deliberate: <see cref="ProcessAsync"/> is sealed and the
/// per-event work happens in <see cref="BuildPayloadAsync"/>. In the ABL, each
/// <c>oms_ir*.cls</c> implements <c>Process</c> itself and repeats the send-and-record
/// sequence, which is why <c>oms_irshup.cls</c> and <c>oms_ircodt.cls</c> have drifted
/// apart in their error handling.
/// </para>
/// </remarks>
public abstract class CommOutProcessorBase : ICommOutProcessor
{
    protected CommOutProcessorBase(IFdm4Sender sender, ILogger logger)
    {
        Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected IFdm4Sender Sender { get; }

    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public virtual string Endpoint => CommStatus.OmsEndpoint;

    /// <inheritdoc />
    public async Task<OperationResult<CommOutResult>> ProcessAsync(
        CommOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var built = await BuildPayloadAsync(request, cancellationToken).ConfigureAwait(false);

        // Assigned to a local so the null check narrows the type for the compiler, and
        // so the value cannot change between the check and the use.
        var payload = built.Value;

        if (built.Failed || payload is null)
        {
            Logger.LogError(
                "{EventType}: payload build failed for transmission {TransmissionId}. {Message}",
                EventType, request.TransmissionId, built.Message);

            return OperationResult<CommOutResult>.Failure(built.Message, built.Code);
        }

        var sent = await Sender
            .SendAsync(
                payload,
                requestIdentifier: $"wms_{EventType.ToLowerInvariant()}",
                fileName: $"{EventType}_{request.TransmissionId}",
                cancellationToken)
            .ConfigureAwait(false);

        if (sent.Failed)
        {
            return OperationResult<CommOutResult>.Failure(sent.Message, sent.Code);
        }

        return OperationResult<CommOutResult>.Success(new CommOutResult(
            OutPayload: payload,
            Response: sent.Value.Body,
            StatusCode: sent.Value.StatusCode));
    }

    /// <summary>
    /// Builds the JSON payload for this event type. The per-processor work.
    /// </summary>
    protected abstract Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken);
}
