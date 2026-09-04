using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Context;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Http;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of <see cref="IWmsHttpClient"/>.
/// </summary>
/// <remarks>
/// Registered with <c>AddHttpClient</c> so retry, circuit breaking and timeout come from
/// <c>Microsoft.Extensions.Http.Resilience</c> rather than the hand-rolled
/// <c>iRetries</c> loop in <c>httpRequestCURL.cls</c>.
/// </remarks>
public sealed class WmsHttpClient : IWmsHttpClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IWmsContext _context;
    private readonly IRequestArchive _archive;
    private readonly ILogger<WmsHttpClient> _logger;

    public WmsHttpClient(
        HttpClient httpClient,
        IWmsContext context,
        IRequestArchive archive,
        ILogger<WmsHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<WmsHttpResponse<TResponse>>> SendAsync<TResponse>(
        WmsHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        if (raw.Failed || raw.Value is null)
        {
            return OperationResult<WmsHttpResponse<TResponse>>.Failure(raw.Message, raw.Code);
        }

        var response = raw.Value;
        TResponse? body = default;

        if (!string.IsNullOrWhiteSpace(response.RawBody))
        {
            try
            {
                body = JsonSerializer.Deserialize<TResponse>(response.RawBody, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "{Endpoint} returned {Status} with a body that is not valid {Type}.",
                    request.EndpointName,
                    response.StatusCode,
                    typeof(TResponse).Name);

                return OperationResult<WmsHttpResponse<TResponse>>.Failure(
                    $"Response from {request.EndpointName} could not be parsed: {ex.Message}",
                    "RESPONSE_PARSE_FAILED");
            }
        }

        return OperationResult<WmsHttpResponse<TResponse>>.Success(new WmsHttpResponse<TResponse>
        {
            StatusCode = response.StatusCode,
            Body = body,
            RawBody = response.RawBody,
            Headers = response.Headers,
            Duration = response.Duration,
        });
    }

    public async Task<OperationResult<WmsHttpResponse<string>>> SendRawAsync(
        WmsHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        using var message = new HttpRequestMessage(request.Method, request.Uri);

        foreach (var (name, value) in request.Headers)
        {
            // TryAddWithoutValidation because several partner APIs use non-standard
            // header names (X-FDM4-API-Key, AUTHENTICATION) that strict parsing rejects.
            message.Headers.TryAddWithoutValidation(name, value);
        }

        message.Headers.TryAddWithoutValidation("X-Correlation-Id", _context.CorrelationId);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
        }

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType)
            {
                CharSet = "utf-8",
            };
        }

        await _archive
            .RecordRequestAsync(request, _context, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (request.Timeout is { } timeout)
            {
                cts.CancelAfter(timeout);
            }

            using var httpResponse = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            var rawBody = await httpResponse.Content
                .ReadAsStringAsync(cts.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            var headers = httpResponse.Headers
                .Concat(httpResponse.Content.Headers)
                .ToImmutableDictionary(
                    h => h.Key,
                    h => string.Join(", ", h.Value),
                    StringComparer.OrdinalIgnoreCase);

            var response = new WmsHttpResponse<string>
            {
                StatusCode = (int)httpResponse.StatusCode,
                Body = rawBody,
                RawBody = rawBody,
                Headers = headers,
                Duration = stopwatch.Elapsed,
            };

            await _archive
                .RecordResponseAsync(request, response, _context, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "{Method} {Endpoint} -> {Status} in {ElapsedMs}ms",
                request.Method,
                request.EndpointName,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return OperationResult<WmsHttpResponse<string>>.Success(response);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "{Endpoint} timed out after {ElapsedMs}ms.",
                request.EndpointName, stopwatch.ElapsedMilliseconds);

            return OperationResult<WmsHttpResponse<string>>.Failure(
                $"{request.EndpointName} timed out after {stopwatch.Elapsed.TotalSeconds:F1}s.",
                "TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();

            // Certificate failures land here. The ABL hid them behind --insecure;
            // surfacing them with a distinct code is the point.
            var code = ex.InnerException is System.Security.Authentication.AuthenticationException
                ? "TLS_VALIDATION_FAILED"
                : "TRANSPORT_FAILED";

            _logger.LogError(ex, "{Endpoint} transport failure ({Code}).",
                request.EndpointName, code);

            return OperationResult<WmsHttpResponse<string>>.Failure(
                $"{request.EndpointName} transport failure: {ex.Message}", code);
        }
    }
}
