using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Platform.Audit;

/// <summary>Registers the append-only audit writer.</summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAuditService"/>.
    /// </summary>
    /// <remarks>
    /// Phase 6 §10 item 3 makes this a Wave 1 deliverable, ahead of any of its 23
    /// writers, because the <c>n_trans.t</c> CREATE trigger it replaces fires for every
    /// writer while a service only fires for callers.
    /// </remarks>
    public static IServiceCollection AddWmsAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
