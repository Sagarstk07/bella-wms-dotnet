using Bella.Wms.Integration.Erp.Contracts;
using Bella.Wms.Integration.Erp.Domain;
using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>
/// Claims OPEN <c>comm_out</c> rows and routes each to its processor.
/// </summary>
/// <remarks>
/// <para>
/// Conversion of <c>api/wms/interface/oms_comm_out_route.p</c> (386 lines).
/// </para>
/// <para>
/// <b>The ABL structure is already correct and is preserved exactly.</b> Three separate
/// transactions per message:
/// </para>
/// <list type="number">
///   <item>
///     <b>Claim</b> (<c>LOCK-BLOCK</c>, lines 75-120). Take an exclusive lock
///     <c>no-wait</c>, re-check the status under the lock, flip <c>O</c> to <c>W</c>,
///     commit. The re-check is what makes two pollers safe against each other.
///   </item>
///   <item>
///     <b>Process</b> (line 132, outside any transaction). The HTTP call to the far
///     system happens with no lock held. This already satisfies Phase 6 §4 rule 3 —
///     unusually for this codebase, so it is worth saying: <c>oms_comm_out_route.p</c>
///     does not need the behavioural change that <c>locusAPI:pick()</c> and the
///     AutoBagger shipping path do.
///   </item>
///   <item>
///     <b>Record</b> (<c>RESPONSE-BLOCK</c>, line 143). Re-lock, write the response,
///     status and duration, commit.
///   </item>
/// </list>
/// <para>
/// <b>What changes.</b> The ABL retries a contended lock up to <c>MAX-LOCK-TRIES</c> (10)
/// with <c>pause RANDOM(1,3)</c> between attempts. Here the claim is a conditional
/// <c>UPDATE ... WHERE process_status = 'O'</c> that either affects one row or none, so
/// contention resolves in one round trip without a retry loop. This is a behavioural
/// difference in mechanism but not in outcome, and it is recorded in
/// <c>docs/ABL_CROSSREF.md</c>.
/// </para>
/// </remarks>
public sealed class CommOutRouter
{
    private static readonly LockOrder ClaimLockOrder = LockOrder.Declare(
        operation: "comm_out claim",
        ablSource: "api/wms/interface/oms_comm_out_route.p:75-120 (LOCK-BLOCK)",
        "comm_out");

    private static readonly LockOrder RecordLockOrder = LockOrder.Declare(
        operation: "comm_out record result",
        ablSource: "api/wms/interface/oms_comm_out_route.p:143-200 (RESPONSE-BLOCK)",
        "comm_out");

    private readonly ICommMessageRepository _repository;
    private readonly CommProcessorRegistry _registry;
    private readonly IWmsCommUnitOfWork _unitOfWork;
    private readonly ILogger<CommOutRouter> _logger;

    public CommOutRouter(
        ICommMessageRepository repository,
        CommProcessorRegistry registry,
        IWmsCommUnitOfWork unitOfWork,
        ILogger<CommOutRouter> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes at most <paramref name="maxMessages"/> open messages.
    /// Returns how many were handled, so the caller can back off when the queue is empty
    /// (the ABL uses <c>IDLE-WAIT-MAX</c> of 5 seconds for this).
    /// </summary>
    public async Task<int> DrainAsync(
        string company,
        string warehouse,
        string endpoint,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var handled = 0;

        while (handled < maxMessages && !cancellationToken.IsCancellationRequested)
        {
            var claimed = await ClaimNextAsync(company, warehouse, endpoint, cancellationToken)
                .ConfigureAwait(false);

            if (claimed is null)
            {
                break;
            }

            await ProcessClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
            handled++;
        }

        return handled;
    }

    /// <summary>Transaction 1 — the claim.</summary>
    private async Task<CommMessage?> ClaimNextAsync(
        string company,
        string warehouse,
        string endpoint,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginAsync(ClaimLockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            var claimed = await _repository
                .ClaimNextOpenOutboundAsync(company, warehouse, endpoint, cancellationToken)
                .ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (claimed is not null)
            {
                _logger.LogInformation(
                    "Claimed comm_out {CommId}: event {EventType}, ref {RefId}.",
                    claimed.CommId, claimed.EventType, claimed.ReferenceId);
            }

            return claimed;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Step 2 (no transaction) and transaction 3 (record the result).</summary>
    private async Task ProcessClaimedAsync(CommMessage message, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        OperationResult<CommOutResult> result;

        if (!_registry.TryResolveOut(message.Endpoint, message.EventType, out var processor))
        {
            result = OperationResult<CommOutResult>.Failure(
                $"No processor registered for {message.Endpoint}:{message.EventType}.",
                "NO_PROCESSOR");
        }
        else
        {
            try
            {
                // No lock is held here. The far-system call is the slow part and the
                // ABL is already correct to keep it outside transaction scope.
                result = await processor
                    .ProcessAsync(
                        new CommOutRequest(
                            message.EventData ?? string.Empty,
                            ReferenceTransactionNumber: 0,
                            message.TransmissionId ?? string.Empty),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The ABL wraps CommProcess in the poller's error handling so one bad
                // message cannot stop the loop. Same posture: the message goes to E and
                // the poller continues.
                _logger.LogError(ex, "Processor threw for comm_out {CommId}.", message.CommId);
                result = OperationResult<CommOutResult>.Failure(ex.Message, "PROCESSOR_THREW");
            }
        }

        var duration = DateTimeOffset.Now - startedAt;

        await _unitOfWork.BeginAsync(RecordLockOrder, cancellationToken).ConfigureAwait(false);

        try
        {
            await _repository.RecordOutboundResultAsync(
                message.CommId,
                status: result.Succeeded ? CommStatus.Complete : CommStatus.Error,
                response: result.Succeeded ? result.Value.Response : result.Message,
                responseStatus: result.Succeeded ? result.Value.StatusCode : 0,
                duration: duration,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "comm_out {CommId} -> {Status} in {DurationMs}ms. {Message}",
            message.CommId,
            result.Succeeded ? CommStatus.Complete : CommStatus.Error,
            (int)duration.TotalMilliseconds,
            result.Message);
    }
}
