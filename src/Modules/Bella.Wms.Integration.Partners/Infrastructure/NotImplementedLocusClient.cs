using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Placeholder <see cref="ILocusClient"/> for Slice 4, before the outbound adapter exists.
/// </summary>
/// <remarks>
/// <para>
/// The inbound handlers need an <see cref="ILocusClient"/> to be constructible —
/// <c>LocusAcceptHandler</c> calls <c>HoldOrderJobAsync</c> on its failure path, matching
/// <c>locusAPI.cls:1777</c>. Registering this stub lets the inbound path be built and
/// tested before Slice 3 delivers the real adapter.
/// </para>
/// <para>
/// <b>It fails loudly, not silently.</b> Each method logs at error level and returns a
/// failed <see cref="OperationResult"/> rather than throwing, so an inbound event that
/// reaches an outbound call produces a visible, attributable failure instead of taking
/// down the request. Swap the registration in
/// <c>PartnersInfrastructureExtensions.AddPartnersInfrastructure</c> when the real client
/// lands; nothing else changes.
/// </para>
/// </remarks>
public sealed class NotImplementedLocusClient : ILocusClient
{
    private readonly ILogger<NotImplementedLocusClient> _logger;

    public NotImplementedLocusClient(ILogger<NotImplementedLocusClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<OperationResult> CreateOrderJobAsync(
        string cartonId, string? priorityOverride = null, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(CreateOrderJobAsync), "locusAPI.cls:362-521", cartonId);

    public Task<OperationResult> UpdateOrderJobAsync(
        string cartonId, int oldPick, int newPick, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(UpdateOrderJobAsync), "locusAPI.cls:523-715", cartonId);

    public Task<OperationResult> CancelOrderJobAsync(
        string cartonId, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(CancelOrderJobAsync), "locusAPI.cls:717-806", cartonId);

    public Task<OperationResult> HoldOrderJobAsync(
        string cartonId, string? message = null, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(HoldOrderJobAsync), "locusAPI.cls:808-899", cartonId);

    public Task<OperationResult> HoldReleaseOrderJobAsync(
        string cartonId, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(HoldReleaseOrderJobAsync), "locusAPI.cls:901-990", cartonId);

    public Task<OperationResult> CreatePutawayJobAsync(
        string sscc18, string robot, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(CreatePutawayJobAsync), "locusAPI.cls:992-1293", sscc18);

    public Task<OperationResult> CreatePutawayJobForToteAsync(
        string toteId, string robot, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(CreatePutawayJobForToteAsync), "locusAPI.cls:1295-1598", toteId);

    public Task<OperationResult> CreatePutawayRejectAsync(
        string toteId, string robot, CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(CreatePutawayRejectAsync), "locusAPI.cls:1600-1654", toteId);

    public Task<OperationResult> UpdatePriorityJobAsync(
        string cartonId, string priorityNumber, string employeeNumber,
        CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(UpdatePriorityJobAsync), "locusAPI.cls:1656-1754", cartonId);

    public Task<OperationResult> SendDuplicateToteAsync(
        string cartonId, bool singleUnit,
        CancellationToken cancellationToken = default) =>
        NotBuilt(nameof(SendDuplicateToteAsync), "locusAPI.cls:953-988", cartonId);

    private Task<OperationResult> NotBuilt(string method, string ablSource, string key)
    {
        _logger.LogError(
            "ILocusClient.{Method} is not built yet (converts {AblSource}). " +
            "Called for '{Key}'. The outbound Locus adapter is Slice 3.",
            method, ablSource, key);

        return Task.FromResult(OperationResult.Failure(
            $"Outbound Locus call {method} is not implemented (converts {ablSource}).",
            "LOCUS_CLIENT_NOT_IMPLEMENTED"));
    }
}
