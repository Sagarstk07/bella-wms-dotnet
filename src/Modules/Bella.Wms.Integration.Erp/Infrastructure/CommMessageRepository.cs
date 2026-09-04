using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Domain;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Erp.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="ICommMessageRepository"/> against <c>wmscomm</c>.
/// </summary>
/// <remarks>
/// <b>SCHEMA-INFERRED throughout.</b> Column names come from the ABL buffers, not a
/// <c>.df</c>. The <c>PUB.</c> schema prefix is the OpenEdge SQL default and may differ.
/// See <c>docs/SCHEMA_REVIEW.md</c>.
/// </remarks>
public sealed class CommMessageRepository : ICommMessageRepository
{
    private const string SelectColumns = """
        comm_id                 AS CommId,
        co_num                  AS Company,
        wh_num                  AS Warehouse,
        comm_endpoint           AS Endpoint,
        event_type              AS EventType,
        event_id                AS EventId,
        event_data              AS EventData,
        process_status          AS ProcessStatus,
        process_date            AS ProcessDate,
        process_time            AS ProcessTime,
        process_duration        AS ProcessDuration,
        process_response        AS ProcessResponse,
        process_response_status AS ProcessResponseStatus,
        create_date             AS CreateDate,
        ref_comm_id             AS ReferenceCommId,
        ref_id                  AS ReferenceId,
        ref_id_type             AS ReferenceIdType,
        transmission_id         AS TransmissionId
        """;

    private readonly IWmsCommUnitOfWork _unitOfWork;
    private readonly ILogger<CommMessageRepository> _logger;

    public CommMessageRepository(IWmsCommUnitOfWork unitOfWork, ILogger<CommMessageRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CommMessage?> ClaimNextOpenOutboundAsync(
        string company, string warehouse, string endpoint, CancellationToken cancellationToken = default) =>
        ClaimNextAsync("comm_out", company, warehouse, endpoint, cancellationToken);

    public Task<CommMessage?> ClaimNextOpenInboundAsync(
        string company, string warehouse, string endpoint, CancellationToken cancellationToken = default) =>
        ClaimNextAsync("comm_in", company, warehouse, endpoint, cancellationToken);

    /// <summary>
    /// Claim-by-conditional-update. The <c>process_status = 'O'</c> predicate in the
    /// WHERE clause is what makes two pollers safe against each other — the same guard
    /// the ABL performs by re-reading the status under an exclusive lock
    /// (<c>oms_comm_out_route.p:96</c>).
    /// </summary>
    private async Task<CommMessage?> ClaimNextAsync(
        string table,
        string company,
        string warehouse,
        string endpoint,
        CancellationToken cancellationToken)
    {
        // Select the oldest candidate first. The ABL uses `repeat preselect each` with no
        // explicit BY, so it takes the default index order; ordering by comm_id here makes
        // the intent explicit and is FIFO, which is what the interface log implies.
        var selectSql = $"""
            SELECT {SelectColumns}
            FROM   PUB.{table}
            WHERE  co_num         = @company
            AND    wh_num         = @warehouse
            AND    comm_endpoint  = @endpoint
            AND    process_status = @openStatus
            ORDER BY comm_id
            FETCH FIRST 1 ROWS ONLY
            """;

        var candidate = await _unitOfWork.Connection.QueryFirstOrDefaultAsync<CommMessage>(
            new CommandDefinition(
                selectSql,
                new { company, warehouse, endpoint, openStatus = CommStatus.Open },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (candidate is null)
        {
            return null;
        }

        var claimSql = $"""
            UPDATE PUB.{table}
            SET    process_status = @wipStatus,
                   process_date   = @processDate,
                   process_time   = @processTime
            WHERE  comm_id        = @commId
            AND    process_status = @openStatus
            """;

        var now = DateTimeOffset.Now;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                claimSql,
                new
                {
                    wipStatus = CommStatus.WorkInProgress,
                    processDate = now.Date,
                    // ABL stores mtime — milliseconds since midnight — in process_time.
                    processTime = (int)now.TimeOfDay.TotalMilliseconds,
                    commId = candidate.CommId,
                    openStatus = CommStatus.Open,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (affected == 0)
        {
            // Another poller claimed it between the select and the update. The ABL's
            // equivalent is `locked(x_comm_out)` or the status re-check failing.
            _logger.LogDebug("comm {CommId} was claimed by another poller.", candidate.CommId);
            return null;
        }

        return candidate with { ProcessStatus = CommStatus.WorkInProgress };
    }

    public Task RecordOutboundResultAsync(
        long commId, string status, string response, int responseStatus,
        TimeSpan duration, CancellationToken cancellationToken = default) =>
        RecordResultAsync("comm_out", commId, status, response, responseStatus, duration, cancellationToken);

    public Task RecordInboundResultAsync(
        long commId, string status, string response, int responseStatus,
        TimeSpan duration, CancellationToken cancellationToken = default) =>
        RecordResultAsync("comm_in", commId, status, response, responseStatus, duration, cancellationToken);

    private async Task RecordResultAsync(
        string table,
        long commId,
        string status,
        string response,
        int responseStatus,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE PUB.{table}
            SET    process_status          = @status,
                   process_response        = @response,
                   process_response_status = @responseStatus,
                   process_duration        = @duration
            WHERE  comm_id                 = @commId
            """;

        await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    status,
                    response,
                    responseStatus,
                    duration = (decimal)duration.TotalSeconds,
                    commId,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> PromoteNewToOpenAsync(
        string company,
        string warehouse,
        string endpoint,
        bool outbound,
        CancellationToken cancellationToken = default)
    {
        var table = outbound ? "comm_out" : "comm_in";

        var sql = $"""
            UPDATE PUB.{table}
            SET    process_status = @openStatus
            WHERE  co_num         = @company
            AND    wh_num         = @warehouse
            AND    comm_endpoint  = @endpoint
            AND    process_status = @newStatus
            """;

        return await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    openStatus = CommStatus.Open,
                    newStatus = CommStatus.New,
                    company,
                    warehouse,
                    endpoint,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task<int> PurgeCompletedAsync(
        string company,
        string warehouse,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "Deferred. Converts api/wms/interface/oms_comm_purge.p (405 lines), which " +
            "exports each message to the business rule 7000 directory before deleting. " +
            "The export format must be preserved for anyone reading archived messages, " +
            "and that format has not been characterised yet. See docs/ABL_CROSSREF.md.");
}
