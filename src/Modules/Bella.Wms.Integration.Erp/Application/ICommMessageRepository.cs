using Bella.Wms.Integration.Erp.Domain;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>
/// Data access for <c>comm_out</c> and <c>comm_in</c> in the <c>wmscomm</c> database.
/// </summary>
public interface ICommMessageRepository
{
    /// <summary>
    /// Atomically claims the next OPEN outbound message, flipping it to WIP.
    /// Returns <see langword="null"/> when the queue is empty or every candidate was
    /// taken by another poller.
    /// </summary>
    /// <remarks>
    /// Replaces the <c>LOCK-BLOCK</c> / <c>LOCK-LOOP</c> in
    /// <c>oms_comm_out_route.p:75-120</c>. The status re-check the ABL performs under the
    /// lock (line 96) becomes a predicate on the UPDATE, so the claim is atomic without a
    /// retry loop.
    /// </remarks>
    Task<CommMessage?> ClaimNextOpenOutboundAsync(
        string company,
        string warehouse,
        string endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>Claims the next OPEN inbound message. Mirrors <c>oms_comm_in_route.p</c>.</summary>
    Task<CommMessage?> ClaimNextOpenInboundAsync(
        string company,
        string warehouse,
        string endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the outcome of an outbound message.
    /// Replaces <c>RESPONSE-BLOCK</c> in <c>oms_comm_out_route.p:143</c>.
    /// </summary>
    Task RecordOutboundResultAsync(
        long commId,
        string status,
        string response,
        int responseStatus,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Records the outcome of an inbound message.</summary>
    Task RecordInboundResultAsync(
        long commId,
        string status,
        string response,
        int responseStatus,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves NEW messages to OPEN after preparation.
    /// Mirrors <c>oms_comm_out_prep.p</c> / <c>oms_comm_in_prep.p</c>.
    /// </summary>
    Task<int> PromoteNewToOpenAsync(
        string company,
        string warehouse,
        string endpoint,
        bool outbound,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes completed messages older than the retention window.
    /// Mirrors <c>oms_comm_purge.p</c> (405 lines), which also exports to the path in
    /// business rule 7000 before deleting.
    /// </summary>
    Task<int> PurgeCompletedAsync(
        string company,
        string warehouse,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
