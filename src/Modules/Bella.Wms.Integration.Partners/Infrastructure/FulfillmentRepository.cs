using System.Globalization;
using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IFulfillmentRepository"/> over
/// <c>PUB.transactions</c>.
/// </summary>
public sealed class FulfillmentRepository : IFulfillmentRepository
{
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<FulfillmentRepository> _logger;

    public FulfillmentRepository(
        IIrmsUnitOfWork unitOfWork,
        ILogger<FulfillmentRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> CloseForMovementAsync(
        string company,
        string warehouse,
        int movementId,
        CancellationToken cancellationToken = default)
    {
        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Closing a fulfillment must run inside the unit of work that completes the " +
                "movement. The ABL does both inside REPLEN-BLOCK (locusAPI.cls:3290-3580).");
        }

        // trans_type and row_status are both one-character columns compared case-
        // insensitively by the ABL and case-sensitively by SQL. Both cases listed rather
        // than UPPER(), which would cost the index.
        const string Sql = """
            UPDATE PUB.transactions
            SET    row_status = 'C'
            WHERE  co_num     = @company
            AND    wh_num     = @warehouse
            AND    trans_type IN ('FM', 'fm')
            AND    link_id    = @linkId
            AND    row_status IN ('O', 'o')
            """;

        var affected = await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                Sql,
                new
                {
                    company,
                    warehouse,
                    linkId = movementId.ToString(CultureInfo.InvariantCulture),
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        // ⚠ The ABL closes exactly one row — FIND FIRST, then assign. This closes every
        // match. Where more than one open FM row shares a link_id the two stacks diverge,
        // and this one is arguably the more correct: leaving the others open means a
        // completed movement still looks outstanding. Flagged rather than made faithful,
        // because "close all fulfillments for a finished move" is defensible and "close an
        // arbitrary one of them" is not.
        if (affected > 1)
        {
            _logger.LogWarning(
                "Closed {Count} open FM transactions for movement {MovementId} in " +
                "{Company}/{Warehouse}. The ABL would have closed one and left the rest.",
                affected, movementId, company, warehouse);
        }

        return affected;
    }
}
