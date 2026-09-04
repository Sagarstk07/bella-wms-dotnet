using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Registers the Partners module's data access and outbound clients (Phase 6 B17).
/// </summary>
/// <remarks>
/// Split from <c>AddPartnersModule</c> — which registers inbound handlers, all of which
/// live in <c>Application</c> — because only <c>Infrastructure</c> may name concrete
/// repositories. A host calls both.
/// </remarks>
public static class PartnersInfrastructureExtensions
{
    /// <summary>Registers repositories and partner clients.</summary>
    public static IServiceCollection AddPartnersInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICartonRepository, CartonRepository>();
        services.AddScoped<IPickRepository, PickRepository>();
        services.AddScoped<IToteTransactionRepository, ToteTransactionRepository>();

        // Stock: what is on the shelves, what is being moved, and who is holding a record.
        // The putaway family (replenputcomplete, putawayput, putawayputcomplete) needs all
        // three together — see docs/HANDLER_SPECS.md.
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();
        services.AddScoped<IVirtualLockRepository, VirtualLockRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IFulfillmentRepository, FulfillmentRepository>();

        // Seam for rf/wsset_cyc.p. Fails loudly until rf/ converts — see the type's remarks.
        services.AddScoped<ICycleCountService, NotImplementedCycleCountService>();

        // Outbound partner clients (ILocusClient, IPyramidClient, IUpsClient,
        // IAutoBaggerClient) have contracts but no implementations yet — they are Slice 3.
        // Registering a throwing stub for ILocusClient keeps LocusAcceptHandler
        // constructible so the inbound path can be exercised end to end before the
        // outbound adapter exists.
        services.AddScoped<ILocusClient, NotImplementedLocusClient>();

        // The reject family (docs/HANDLER_SPECS.md Group 2) sends an alert email on
        // rejection. No mail transport exists yet — same placeholder-stub treatment as
        // ILocusClient above.
        services.AddScoped<INotificationService, NotImplementedNotificationService>();

        return services;
    }
}
