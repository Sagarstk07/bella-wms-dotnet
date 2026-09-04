using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Identity;

namespace Bella.Wms.Api.Middleware;

/// <summary>
/// Populates <see cref="WmsContext"/> at the API boundary.
/// </summary>
/// <remarks>
/// <para>
/// Phase 6 §10 item 5: "The ambient context object — mirroring <c>static_connect</c>,
/// populated at the API boundary."
/// </para>
/// <para>
/// Replaces <c>wsLocusAPI.p:102-125</c> and <c>wsMiddlewareAPI.p:199-215,304-315</c>.
/// </para>
/// <para>
/// <b>Two behavioural differences, both deliberate.</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>An unresolvable employee is a hard failure.</b> <c>wsLocusAPI.p:124-125</c>
///     logs "employee master ... CAN NOT BE FOUND" and continues with an uninitialised
///     <c>static_connect</c>, so the request runs against whatever company and warehouse
///     the previous request on that PASOE agent left behind. That is a cross-tenant data
///     hazard, not a quirk. Here it is a 401.
///   </item>
///   <item>
///     <b>No employee is created during authentication.</b>
///     <c>wsMiddlewareAPI.p:429-456</c> <c>ensureEmployee</c> creates an <c>empmst</c>
///     row inside the auth path when the API user is missing. Provisioning is a
///     deliberate administrative act, not something an inbound request performs — see
///     <c>docs/ABL_CROSSREF.md</c> for the migration note.
///   </item>
/// </list>
/// </remarks>
public sealed class WmsContextMiddleware
{
    private const string CompanyHeader = "HTTP_COMPANY";
    private const string WarehouseHeader = "HTTP_WAREHOUSE";
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<WmsContextMiddleware> _logger;

    public WmsContextMiddleware(RequestDelegate next, ILogger<WmsContextMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext httpContext, WmsContext context, IEmployeeLookup employees)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(employees);

        // The middleware channel takes company and warehouse from the JSON body rather
        // than headers (wsMiddlewareAPI.p:200-204, "get company from JSON payload not
        // the header"), so those routes initialise the context themselves and this
        // middleware stands aside.
        if (httpContext.Request.Path.StartsWithSegments("/api/middleware"))
        {
            await _next(httpContext).ConfigureAwait(false);
            return;
        }

        var company = httpContext.Request.Headers[CompanyHeader].FirstOrDefault();
        var warehouse = httpContext.Request.Headers[WarehouseHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(warehouse))
        {
            _logger.LogWarning("Request to {Path} carried no company/warehouse headers.",
                httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("Missing company or warehouse.").ConfigureAwait(false);
            return;
        }

        // ABL wsLocusAPI.p:105-111 resolves the employee whose emp_num BEGINS "LOCUS".
        var apiUser = ResolveApiUserPrefix(httpContext.Request.Path);

        var employee = await employees
            .FindByPrefixAsync(company, warehouse, apiUser, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (employee is null)
        {
            _logger.LogError(
                "No '{Prefix}' employee for {Company}/{Warehouse}. Rejecting.",
                apiUser, company, warehouse);

            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response
                .WriteAsync($"No API user '{apiUser}' configured for {company}/{warehouse}.")
                .ConfigureAwait(false);
            return;
        }

        var correlationId = httpContext.Request.Headers[CorrelationHeader].FirstOrDefault();

        context.Initialise(company, warehouse, employee.EmployeeNumber, correlationId: correlationId);
        context.ApplyEmployee(employee.DepartmentNumber, employee.ShiftNumber);
        context.ApplyOrigin(httpContext.Request.Path.Value ?? string.Empty);

        await _next(httpContext).ConfigureAwait(false);
    }

    private static string ResolveApiUserPrefix(PathString path) =>
        path.StartsWithSegments("/api/pyramid") ? "PYRAMID" : "LOCUS";
}
