using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Application.Processors;
using Bella.Wms.Integration.Erp.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Integration.Erp.Infrastructure;

/// <summary>
/// Registers the ERP integration module (Phase 6 B16).
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Infrastructure</c> because it is the only layer that may name concrete
/// repositories. <c>Application</c> depends on <see cref="ICommMessageRepository"/>;
/// nothing in <c>Application</c> knows <see cref="CommMessageRepository"/> exists.
/// </para>
/// <para>
/// Both hosts call this. <c>Bella.Wms.Jobs</c> runs the pollers;
/// <c>Bella.Wms.Api</c> needs the same services for the inbound interface endpoint and
/// for diagnostics that list registered routes.
/// </para>
/// </remarks>
public static class ErpServiceCollectionExtensions
{
    /// <summary>Registers the comm queue, the processor registry and the FDM4 sender.</summary>
    public static IServiceCollection AddErpModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommMessageRepository, CommMessageRepository>();
        services.AddScoped<CommProcessorRegistry>();
        services.AddScoped<CommOutRouter>();
        services.AddScoped<IFdm4Sender, Fdm4Sender>();

        // --- Outbound processors. Registration is the route table; WMSCommOutProcess.cls
        // had a CASE statement instead, which is why adding a processor there meant
        // editing a shared file. ---

        // Converted: pass-through in the ABL, so genuinely complete.
        services.AddScoped<ICommOutProcessor, OrderDownloadAckProcessor>();  // IRCODT
        services.AddScoped<ICommOutProcessor, OrderDropUpdateProcessor>();   // IRORUP

        // Scaffolded: these build payloads and are blocked on the .df schema plus a
        // captured sample of what FDM4 currently receives. They throw with the ABL
        // reference rather than silently returning an empty payload.
        services.AddScoped<ICommOutProcessor, ShipmentUpdateProcessor>();    // IRSHUP
        services.AddScoped<ICommOutProcessor, PackedUpdateProcessor>();      // IRPMUP
        services.AddScoped<ICommOutProcessor, PickingUpdateProcessor>();     // IRPIUP

        // No inbound processors yet. oms_ircodn.cls (IRCODN, 1,523 lines) is Slice 2.
        // CommProcessorRegistry takes IEnumerable<ICommInProcessor>, which resolves to an
        // empty sequence — the registry handles that and logs an unregistered route
        // rather than failing to construct.

        return services;
    }
}
