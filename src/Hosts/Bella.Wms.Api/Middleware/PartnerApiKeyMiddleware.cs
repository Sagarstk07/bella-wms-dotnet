using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Bella.Wms.Api.Middleware;

/// <summary>Per-channel inbound API keys, supplied by configuration — never literals.</summary>
/// <remarks>
/// <para>
/// <b>What this replaces, and why it matters.</b> Every ABL router holds its inbound key
/// in source:
/// </para>
/// <list type="bullet">
///   <item><c>wsLocusAPI.p:28</c> — <c>def var sApiKey as char init "OJsLL7IuTp"</c>.</item>
///   <item><c>wsPyramidAPI.p:22</c> — <c>def var sApiKey as char init "j5gBhMJ3fJ"</c>.</item>
///   <item><c>middleware.i:16</c> — <c>INBOUND_API_KEY64</c>, a base64 literal.</item>
///   <item><c>rest/inbound.p:37</c> — a different base64 literal.</item>
///   <item><c>interface/config.json:6</c> — repeats the <c>wsLocusAPI.p</c> key.</item>
/// </list>
/// <para>
/// Base64 is an encoding, not encryption, so the two <c>*64</c> constants are plaintext
/// keys with extra steps. All of these values are in version control and have now moved
/// between machines; they need rotating regardless of the conversion timeline.
/// </para>
/// </remarks>
public sealed class PartnerApiKeyOptions
{
    public const string SectionName = "PartnerApiKeys";

    /// <summary>Channel name to expected key. Bound from configuration at startup.</summary>
    public IReadOnlyDictionary<string, string> Keys { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Validates the inbound API key for partner channels.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the auth blocks in <c>wsLocusAPI.p:83-100</c>,
/// <c>wsMiddlewareAPI.p:93-139</c> and <c>wsPyramidAPI.p:90-100</c>.
/// </para>
/// <para>
/// <b>Three differences from the ABL.</b> The comparison is constant-time, so the
/// endpoint does not leak key content through response timing. The key never appears in
/// a log — <c>wsMiddlewareAPI.p:119</c> logs the rejected key verbatim, which puts
/// near-miss credentials in the application log. And the header name is preserved exactly
/// (<c>AUTHENTICATION</c>, not <c>Authorization</c>) because the partners are sending it
/// and that is contract, however unusual it looks.
/// </para>
/// </remarks>
public sealed class PartnerApiKeyMiddleware
{
    private const string AuthHeader = "AUTHENTICATION";

    private readonly RequestDelegate _next;
    private readonly PartnerApiKeyOptions _options;
    private readonly ILogger<PartnerApiKeyMiddleware> _logger;

    public PartnerApiKeyMiddleware(
        RequestDelegate next,
        IOptions<PartnerApiKeyOptions> options,
        ILogger<PartnerApiKeyMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var channel = ResolveChannel(httpContext.Request.Path);

        if (channel is null)
        {
            await _next(httpContext).ConfigureAwait(false);
            return;
        }

        if (!_options.Keys.TryGetValue(channel, out var expectedKey) ||
            string.IsNullOrWhiteSpace(expectedKey))
        {
            // Fail closed. A channel with no configured key must not be open.
            _logger.LogError("No inbound API key configured for channel {Channel}.", channel);
            await RejectAsync(httpContext).ConfigureAwait(false);
            return;
        }

        var presented = ExtractKey(httpContext.Request.Headers[AuthHeader].FirstOrDefault());

        if (presented is null || !FixedTimeEquals(presented, expectedKey))
        {
            // Deliberately does not log the presented value.
            _logger.LogWarning("Rejected inbound request on {Channel}: invalid key.", channel);
            await RejectAsync(httpContext).ConfigureAwait(false);
            return;
        }

        await _next(httpContext).ConfigureAwait(false);
    }

    /// <summary>
    /// Accepts both forms the ABL accepts: a bare key, or <c>basic &lt;base64&gt;</c>
    /// which it base64-decodes (<c>wsLocusAPI.p:86-90</c>,
    /// <c>wsMiddlewareAPI.p:104-128</c>).
    /// </summary>
    private static string? ExtractKey(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        var parts = headerValue.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return headerValue.Trim();
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(expected));

    private static async Task RejectAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Body text matches the ABL exactly — partners may be matching on it.
        await httpContext.Response.WriteAsync("Invalid Authentication").ConfigureAwait(false);
    }

    private static string? ResolveChannel(PathString path) => path switch
    {
        _ when path.StartsWithSegments("/api/locus") => "locus",
        _ when path.StartsWithSegments("/api/middleware") => "middleware",
        _ when path.StartsWithSegments("/api/pyramid") => "pyramid",
        _ => null,
    };
}
