using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Placeholder for <see cref="ICycleCountService"/> until <c>rf/</c> converts.
/// </summary>
/// <remarks>
/// Returns a failure rather than succeeding quietly. A silent success would let a handler
/// commit a stock movement that left inventory negative, with nothing flagged for counting
/// — the warehouse would carry a wrong number and nobody would be told. A loud failure
/// stops the operation, which is what the ABL does when <c>wsset_cyc.p</c> errors.
/// </remarks>
public sealed class NotImplementedCycleCountService : ICycleCountService
{
    private readonly ILogger<NotImplementedCycleCountService> _logger;

    public NotImplementedCycleCountService(ILogger<NotImplementedCycleCountService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<OperationResult> FlagForCountAsync(
        string company,
        string warehouse,
        string employeeNumber,
        int inventoryId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Cycle count requested for inventory {InventoryId} in {Company}/{Warehouse} " +
            "but ICycleCountService is not implemented (converts rf/wsset_cyc.p). " +
            "Stock has gone negative and nothing will be counted.",
            inventoryId, company, warehouse);

        return Task.FromResult(OperationResult.Failure(
            "Cycle counting is not implemented yet (converts rf/wsset_cyc.p). " +
            "Inventory has gone negative and cannot be flagged for counting.",
            "CYCLE_COUNT_NOT_IMPLEMENTED"));
    }
}
