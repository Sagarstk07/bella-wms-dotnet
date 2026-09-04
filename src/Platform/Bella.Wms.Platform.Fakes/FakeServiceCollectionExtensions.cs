using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Bella.Wms.Platform.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bella.Wms.Platform.Fakes;

/// <summary>
/// Replaces every database-backed service with an in-memory fake.
/// </summary>
public static class FakeServiceCollectionExtensions
{
    /// <summary>
    /// Swaps the real data access for fakes so the host runs with no database.
    /// </summary>
    /// <param name="services">The service collection, already configured with the real services.</param>
    /// <param name="isDevelopment">
    /// Must be <see langword="true"/>. Passing <see langword="false"/> throws.
    /// </param>
    /// <param name="warehouse">
    /// Optional seeded warehouse. Defaults to <see cref="InMemoryWarehouse.CreateSeeded"/>.
    /// Pass your own to assert against it from a test.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The guard is deliberate and should not be softened.</b> This is a warehouse
    /// system: a host that silently started against fake inventory would accept picks,
    /// write audit rows and answer Locus with confident nonsense. The failure mode of
    /// forgetting to turn this off is worse than the inconvenience of it throwing, so it
    /// throws — loudly, at startup, before anything can serve a request.
    /// </para>
    /// <para>
    /// Two independent conditions must both hold: the host must be in the Development
    /// environment, and configuration must explicitly set <c>UseFakeData</c> to true.
    /// Neither alone is enough.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddWmsFakes(
        this IServiceCollection services,
        bool isDevelopment,
        InMemoryWarehouse? warehouse = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                "AddWmsFakes was called outside the Development environment. In-memory " +
                "fakes must never back a running warehouse — a host serving fake " +
                "inventory would answer Locus and write audit rows for stock that does " +
                "not exist. Remove the UseFakeData setting from this environment.");
        }

        var seeded = warehouse ?? InMemoryWarehouse.CreateSeeded();
        services.AddSingleton(seeded);

        // The fake unit of work is registered concretely as well as under both database
        // interfaces, so FakeAuditService can observe the same instance's transaction
        // state. Scoped, matching the real registration.
        services.RemoveAll<IIrmsUnitOfWork>();
        services.RemoveAll<IWmsCommUnitOfWork>();
        services.AddScoped<FakeUnitOfWork>();
        services.AddScoped<IIrmsUnitOfWork>(sp => sp.GetRequiredService<FakeUnitOfWork>());
        services.AddScoped<IWmsCommUnitOfWork>(sp => sp.GetRequiredService<FakeUnitOfWork>());

        services.RemoveAll<ISequenceAllocator>();
        services.AddScoped<ISequenceAllocator, FakeSequenceAllocator>();

        services.RemoveAll<IEmployeeLookup>();
        services.AddScoped<IEmployeeLookup, FakeEmployeeLookup>();

        services.RemoveAll<ICartonRepository>();
        services.AddScoped<ICartonRepository, FakeCartonRepository>();

        services.RemoveAll<IPickRepository>();
        services.AddScoped<IPickRepository, FakePickRepository>();

        services.RemoveAll<IToteTransactionRepository>();
        services.AddScoped<IToteTransactionRepository, FakeToteTransactionRepository>();

        // Stock. Its own store for now — see InMemoryStock's remarks. Fold it into
        // InMemoryWarehouse when a handler needs stock and cartons to agree.
        services.AddSingleton(InMemoryStock.CreateSeeded());

        services.RemoveAll<IInventoryRepository>();
        services.AddScoped<IInventoryRepository, FakeInventoryRepository>();

        services.RemoveAll<IMovementRepository>();
        services.AddScoped<IMovementRepository, FakeMovementRepository>();

        services.RemoveAll<IVirtualLockRepository>();
        services.AddScoped<IVirtualLockRepository, FakeVirtualLockRepository>();

        services.RemoveAll<IItemRepository>();
        services.AddScoped<IItemRepository, FakeItemRepository>();

        services.RemoveAll<IFulfillmentRepository>();
        services.AddScoped<IFulfillmentRepository, FakeFulfillmentRepository>();

        services.RemoveAll<IBusinessRuleService>();
        services.AddScoped<IBusinessRuleService, FakeBusinessRuleService>();

        services.RemoveAll<IAuditService>();
        services.AddScoped<IAuditService, FakeAuditService>();

        return services;
    }
}
