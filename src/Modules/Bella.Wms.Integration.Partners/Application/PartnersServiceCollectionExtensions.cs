using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>Registers the Partners module.</summary>
public static class PartnersServiceCollectionExtensions
{
    /// <summary>
    /// Registers every inbound handler and the routers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This method is the API surface.</b> Because the ABL dispatches by reflection,
    /// adding a public method to <c>locusAPI.cls</c> silently adds an endpoint. Here,
    /// an endpoint exists only if it is registered on this list, and the contract test
    /// <c>InboundSurfaceTests</c> asserts the registered set matches
    /// <c>docs/API_SURFACE.md</c>.
    /// </para>
    /// <para>
    /// Deferred handlers are registered deliberately. They throw
    /// <see cref="NotImplementedException"/> with the ABL reference when called, which is
    /// better than a silent 200 "Invalid Endpoint Action" that would look to Locus like
    /// the event was rejected rather than not yet built.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPartnersModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<InboundEventRegistry>();
        services.AddScoped<LocusEventRouter>();

        // --- Locus: convertible now (Slice 4) ---
        services.AddScoped<IInboundEventHandler, LocusAcceptHandler>();
        services.AddScoped<IInboundEventHandler, LocusRejectHandler>();
        services.AddScoped<IInboundEventHandler, LocusHoldCompleteHandler>();
        services.AddScoped<IInboundEventHandler, LocusHoldRejectHandler>();
        services.AddScoped<IInboundEventHandler, LocusHoldReleaseCompleteHandler>();
        services.AddScoped<IInboundEventHandler, LocusHoldReleaseRejectHandler>();
        services.AddScoped<IInboundEventHandler, LocusUpdateCompleteHandler>();
        services.AddScoped<IInboundEventHandler, LocusUpdateRejectHandler>();
        services.AddScoped<IInboundEventHandler, LocusCancelCompleteHandler>();
        services.AddScoped<IInboundEventHandler, LocusCancelRejectHandler>();

        // --- Locus: tote handling, pending the wsset_cyc.p ownership decision ---
        services.AddScoped<IInboundEventHandler, LocusToteInductHandler>();
        services.AddScoped<IInboundEventHandler, LocusToteMoveHandler>();
        services.AddScoped<IInboundEventHandler, LocusToteInductCancelHandler>();

        // --- Locus: putaway and replenishment ---
        services.AddScoped<IInboundEventHandler, LocusPutawayRequestHandler>();
        services.AddScoped<IInboundEventHandler, LocusPutawayAcceptHandler>();
        services.AddScoped<IInboundEventHandler, LocusPutawayRejectHandler>();
        services.AddScoped<IInboundEventHandler, LocusPutawayPutInductHandler>();

        // putawayput delegates to releaseReplenishment when the tote is carrying a
        // replenishment rather than a putaway (locusAPI.cls:3641-3651). The service is
        // scoped so it shares the handler's unit of work — the ABL runs both inside one
        // REPLEN-BLOCK transaction.
        services.AddScoped<ReplenishmentReleaseService>();
        services.AddScoped<IInboundEventHandler, LocusPutawayPutHandler>();

        // Registered concretely as well as behind IInboundEventHandler, because
        // LocusPutawayPutCompleteHandler forwards to it — mirroring the ABL, where
        // putawayputcomplete is a four-line delegation to replenputcomplete
        // (locusAPI.cls:4486-4492). Scoped, so both share the request's unit of work.
        services.AddScoped<LocusReplenPutCompleteHandler>();
        services.AddScoped<IInboundEventHandler>(
            sp => sp.GetRequiredService<LocusReplenPutCompleteHandler>());
        services.AddScoped<IInboundEventHandler, LocusPutawayPutCompleteHandler>();

        // --- Locus: deferred until picking converts ---
        services.AddScoped<IInboundEventHandler, LocusPickHandler>();
        services.AddScoped<IInboundEventHandler, LocusPickCompleteHandler>();

        // NOT REGISTERED, deliberately: locusAPI.cls:3188 `testtotemove`. It is a test
        // hook that reflection dispatch makes reachable from production traffic. It has
        // no place on the API surface and is left off. If Locus is genuinely calling it,
        // captured traffic will show that and the decision can be revisited.

        return services;
    }
}
