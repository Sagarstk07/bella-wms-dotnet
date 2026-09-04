using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Platform.Config;

/// <summary>Registers <c>syspar_value</c> access.</summary>
public static class ConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers the business rule service and its request-scoped cache.
    /// </summary>
    /// <remarks>
    /// Both are scoped. <see cref="RequestScopedRuleCache"/> as a singleton would cache
    /// configuration across requests and break the Phase 6 §9 requirement that both
    /// stacks read <c>syspar_value</c> live.
    /// </remarks>
    public static IServiceCollection AddWmsConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RequestScopedRuleCache>();
        services.AddScoped<IBusinessRuleService, BusinessRuleService>();

        return services;
    }
}
