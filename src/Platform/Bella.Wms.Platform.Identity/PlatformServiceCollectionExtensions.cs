using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Platform.Identity;

/// <summary>
/// Registers the Wave 1 platform primitives. Every host calls this first.
/// </summary>
/// <remarks>
/// <para>
/// This lives in <c>Platform.Identity</c> because it is the platform project that
/// references the most others — putting it in <c>Abstractions</c> would invert the
/// dependencies. It is the one place that knows the full platform composition.
/// </para>
/// <para>
/// <b>Lifetimes are the load-bearing decision here.</b> Everything below is scoped, and
/// that scoping is what replaces <c>wms_webhost/wmswipeout.p</c> — the ABL routers must
/// call it in a <c>FINALLY</c> block on every request because <c>static_connect</c> is
/// process-static and PASOE reuses agents. A scoped object cannot leak into the next
/// request.
/// </para>
/// <para>
/// Registering any of these as singletons would reintroduce exactly the bug
/// <c>wmswipeout.p</c> exists to paper over, and it would do so silently.
/// </para>
/// </remarks>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ambient context, both units of work, and the identity lookup.
    /// </summary>
    public static IServiceCollection AddWmsPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // --- Ambient context. One instance per request, exposed under both types so a
        // consumer can read it (IWmsContext) or populate it (WmsContext) without two
        // objects existing. ---
        services.AddScoped<WmsContext>();
        services.AddScoped<IWmsContext>(sp => sp.GetRequiredService<WmsContext>());

        // --- Units of work. Two databases, two types, no factory needed. ---
        // 51 of the 55 sequences in irms.df cycle, so every generated id has to be drawn
        // and checked. Scoped: it draws inside the caller's unit of work.
        services.AddScoped<ISequenceAllocator, SequenceAllocator>();

        services.AddScoped<IIrmsUnitOfWork, IrmsUnitOfWork>();
        services.AddScoped<IWmsCommUnitOfWork, WmsCommUnitOfWork>();

        // --- Identity (Phase 6 B15 stub). ---
        services.AddScoped<IEmployeeLookup, EmployeeRepository>();

        return services;
    }
}
