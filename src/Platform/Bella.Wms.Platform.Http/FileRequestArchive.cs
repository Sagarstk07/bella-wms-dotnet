using System.Text.RegularExpressions;
using Bella.Wms.Platform.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bella.Wms.Platform.Http;

public sealed class RequestArchiveOptions
{
    public const string SectionName = "RequestArchive";

    /// <summary>
    /// Archive root. Mirrors ABL business rule 2013 ("Unix Base Directory"), which
    /// <c>wsMiddlewareAPI.p:321</c> reads via <c>static_connect:getRule(2013)</c>.
    /// </summary>
    public string RootPath { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Writes payloads to the same directory layout the ABL uses, with redaction.
/// </summary>
public sealed partial class FileRequestArchive : IRequestArchive
{
    private readonly RequestArchiveOptions _options;
    private readonly ILogger<FileRequestArchive> _logger;

    public FileRequestArchive(
        IOptions<RequestArchiveOptions> options,
        ILogger<FileRequestArchive> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RecordRequestAsync(
        WmsHttpRequest request,
        IWmsContext context,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            channel: request.EndpointName,
            direction: "outbound/requests",
            action: request.Method.Method,
            correlationKey: request.IdempotencyKey ?? context.CorrelationId,
            payload: request.Body ?? string.Empty,
            context,
            cancellationToken);

    public Task RecordResponseAsync(
        WmsHttpRequest request,
        WmsHttpResponse<string> response,
        IWmsContext context,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            channel: request.EndpointName,
            direction: "outbound/responses",
            action: $"{request.Method.Method}_{response.StatusCode}",
            correlationKey: request.IdempotencyKey ?? context.CorrelationId,
            payload: response.RawBody,
            context,
            cancellationToken);

    public Task RecordInboundAsync(
        string channel,
        string action,
        string correlationKey,
        string payload,
        IWmsContext context,
        CancellationToken cancellationToken = default) =>
        WriteAsync(channel, "inbound/requests", action, correlationKey, payload, context, cancellationToken);

    public Task RecordInboundResponseAsync(
        string channel,
        string action,
        string correlationKey,
        string payload,
        IWmsContext context,
        CancellationToken cancellationToken = default) =>
        WriteAsync(channel, "inbound/responses", action, correlationKey, payload, context, cancellationToken);

    private async Task WriteAsync(
        string channel,
        string direction,
        string action,
        string correlationKey,
        string payload,
        IWmsContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.RootPath))
        {
            return;
        }

        try
        {
            // Layout mirrors wsMiddlewareAPI.p:331:
            //   {root}/{channel}/{company}/{warehouse}/{direction}/
            var directory = Path.Combine(
                _options.RootPath,
                Sanitise(channel),
                context.Company.ToLowerInvariant(),
                context.Warehouse.ToLowerInvariant(),
                direction.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(directory);

            // Filename mirrors the ABL: {action}_{key}_{timestamp}.json.
            // ABL used string(mtime) plus random(1,9999); a UTC timestamp with
            // milliseconds plus the correlation id is unique without the collision risk.
            var fileName =
                $"{Sanitise(action)}_{Sanitise(correlationKey)}_" +
                $"{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";

            var path = Path.Combine(directory, fileName);

            await File.WriteAllTextAsync(path, Redact(payload), cancellationToken)
                      .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The ABL used `copy-lob ... no-error`, so an archive failure never broke the
            // request. Same posture here: log and continue.
            _logger.LogWarning(ex, "Could not archive {Direction} payload for {Channel}.",
                direction, channel);
        }
    }

    /// <summary>
    /// Removes credentials before anything reaches disk. The ABL archived payloads
    /// verbatim, so bearer tokens and basic-auth headers were written in clear text.
    /// </summary>
    private static string Redact(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var redacted = SecretJsonPropertyRegex().Replace(payload, "\"$1\": \"[REDACTED]\"");
        return AuthorizationHeaderRegex().Replace(redacted, "$1 [REDACTED]");
    }

    private static string Sanitise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var cleaned = UnsafeFileCharsRegex().Replace(value, "_");
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    [GeneratedRegex(
        "\"(password|apiPassword|apiKey|api_key|token|access_token|secret|authorization)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretJsonPropertyRegex();

    [GeneratedRegex(@"(?i)\b(basic|bearer)\s+[A-Za-z0-9\-._~+/=]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"[^A-Za-z0-9_\-\.]")]
    private static partial Regex UnsafeFileCharsRegex();
}
