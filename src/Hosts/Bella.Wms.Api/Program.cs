using Bella.Wms.Api.Endpoints;
using Bella.Wms.Api.Middleware;
using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Infrastructure;
using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Infrastructure;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Data;
using Bella.Wms.Platform.Fakes;
using Bella.Wms.Platform.Http;
using Bella.Wms.Platform.Identity;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Options. Every secret comes from the configuration chain — environment, user
// secrets or a vault. None is a literal, unlike the ABL routers which compile
// their inbound keys into source (wsLocusAPI.p:28, wsPyramidAPI.p:22,
// middleware.i:16, rest/inbound.p:37).
// ---------------------------------------------------------------------------
builder.Services.Configure<OpenEdgeOptions>(
    builder.Configuration.GetSection(OpenEdgeOptions.SectionName));
builder.Services.Configure<RequestArchiveOptions>(
    builder.Configuration.GetSection(RequestArchiveOptions.SectionName));
builder.Services.Configure<PartnerApiKeyOptions>(
    builder.Configuration.GetSection(PartnerApiKeyOptions.SectionName));
builder.Services.Configure<Fdm4Options>(
    builder.Configuration.GetSection(Fdm4Options.SectionName));

// ---------------------------------------------------------------------------
// Platform. Everything scoped — that scoping is what replaces
// wms_webhost/wmswipeout.p, which the ABL routers must call in a FINALLY block
// on every request because static_connect is process-static.
// ---------------------------------------------------------------------------
builder.Services.AddWmsPlatform();   // context, both units of work, empmst lookup
builder.Services.AddWmsConfig();     // syspar_value, request-scoped cache
builder.Services.AddWmsAudit();      // transactions append-only writer
builder.Services.AddWmsHttp();       // HttpClient transport, TLS validation ON

// ---------------------------------------------------------------------------
// Modules.
// ---------------------------------------------------------------------------
builder.Services.AddPartnersModule();          // inbound handler registrations
builder.Services.AddPartnersInfrastructure();  // repositories, partner clients
builder.Services.AddErpModule();               // comm queue, processors, FDM4 sender

// ---------------------------------------------------------------------------
// Fake data. Last, so it can RemoveAll the real registrations above.
//
// Two independent conditions, per docs/RUNNING.md: the UseFakeData flag must be
// set (appsettings.Development.json sets it; appsettings.json does not), AND the
// host must be in Development. The flag alone is not enough — a Release
// deployment that inherited the setting reaches AddWmsFakes with
// isDevelopment: false, and it throws at startup rather than serving fake
// inventory to the warehouse floor.
// ---------------------------------------------------------------------------
if (builder.Configuration.GetValue<bool>("UseFakeData"))
{
    builder.Services.AddWmsFakes(builder.Environment.IsDevelopment());
}

var app = builder.Build();

// Order matters: authenticate before establishing identity-derived context.
//
// Both run only under /api. They are partner-traffic middleware: PartnerApiKeyMiddleware
// demands the AUTHENTICATION header and WmsContextMiddleware demands HTTP_COMPANY and
// HTTP_WAREHOUSE, rejecting with 400 when they are absent. Applied to the whole pipeline
// they also reject /health, which a load balancer probe will never send those headers to.
// UseWhen branches and rejoins, so endpoint routing below is unaffected.
app.UseWhen(
    static httpContext => httpContext.Request.Path.StartsWithSegments("/api"),
    static partnerTraffic =>
    {
        partnerTraffic.UseMiddleware<PartnerApiKeyMiddleware>();
        partnerTraffic.UseMiddleware<WmsContextMiddleware>();
    });

// Unauthenticated by design — a health probe cannot present a partner API key. It must
// therefore never report anything about warehouse data or configuration; the fake-data
// flag is here precisely so an operator can see at a glance that a host is not real.
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    fakeData = app.Configuration.GetValue<bool>("UseFakeData"),
    environment = app.Environment.EnvironmentName,
}))
.WithName("Health")
.WithSummary("Liveness probe. Reports whether this host is running on in-memory fakes.");

app.MapLocusEndpoints();

// Log the route table at startup. The ABL had none — the effective surface was
// every public method on the target class, discoverable only by reading it.
// CreateAsyncScope, not CreateScope. IUnitOfWork is IAsyncDisposable, and a scope
// disposed synchronously throws InvalidOperationException the moment it holds a service
// that implements only the async form. This is not a fakes-only problem: the real
// OpenEdgeUnitOfWork is async-dispose too, so a sync scope here would have thrown against
// a real database as well. Nothing caught it because the host had never been started.
await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    if (builder.Configuration.GetValue<bool>("UseFakeData"))
    {
        logger.LogWarning(
            "RUNNING WITH IN-MEMORY FAKE DATA. No database is connected and no result " +
            "from this host is real.");
    }

    var inbound = scope.ServiceProvider.GetRequiredService<InboundEventRegistry>();
    logger.LogInformation(
        "Inbound partner routes ({Count}): {Routes}",
        inbound.RegisteredRoutes.Count,
        string.Join(", ", inbound.RegisteredRoutes.Order()));

    var erp = scope.ServiceProvider.GetRequiredService<CommProcessorRegistry>();
    logger.LogInformation(
        "ERP outbound routes ({Count}): {Routes}",
        erp.RegisteredOutboundRoutes.Count,
        string.Join(", ", erp.RegisteredOutboundRoutes.Order()));
}

await app.RunAsync();

/// <summary>
/// Exists only so <c>WebApplicationFactory&lt;Program&gt;</c> in
/// <c>LocusEndpointTests</c> can name this host's entry point. Top-level statements
/// generate an <c>internal</c> <c>Program</c> class, which a test assembly cannot see;
/// this makes it public without otherwise changing the host.
/// </summary>
public partial class Program { }
