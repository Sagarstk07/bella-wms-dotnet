using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Platform.Context;

namespace Bella.Wms.Api.Endpoints;

/// <summary>
/// The inbound Locus endpoint. Replaces <c>api/wms/wsLocusAPI.p</c>.
/// </summary>
public static class LocusEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/locus</c>, the .NET equivalent of the WebSpeed endpoint
    /// <c>/cgi-bin/.../api/wms/wsLocusAPI.p</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reverse proxy in front of both stacks (Phase 6 §9) decides whether a Locus
    /// callback goes here or to the existing WebSpeed broker. Until this endpoint has run
    /// clean in production for the agreed period, the ABL path stays deployed and the
    /// routing table is the rollback switch.
    /// </para>
    /// <para>
    /// <b>What moved out of the ABL router.</b> <c>wsLocusAPI.p:59-129</c> does four
    /// things before dispatch: authenticate, resolve the company/warehouse headers, look
    /// up the LOCUS employee and initialise <c>static_connect</c>, then parse the body.
    /// Here, authentication is middleware, context initialisation is
    /// <see cref="Middleware.WmsContextMiddleware"/>, and only routing is left.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapLocusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locus")
                       .WithTags("Locus");

        group.MapPost("/", async (
            HttpRequest httpRequest,
            LocusEventRouter router,
            IWmsContext context,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(httpRequest.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var result = await router.RouteAsync(payload, cancellationToken).ConfigureAwait(false);

            // The ABL sets Content-Length from length(lcResponse, "RAW") because PHP cURL
            // was truncating double-byte data (wsLocusAPI.p:54, BEL010100). Writing the
            // body as UTF-8 bytes gives the same result without the workaround.
            return Results.Content(result.Body, "application/json", System.Text.Encoding.UTF8, result.StatusCode);
        })
        .WithName("LocusCallback")
        .WithSummary("Receives Locus Robotics order job and putaway job callbacks.");

        return app;
    }
}
