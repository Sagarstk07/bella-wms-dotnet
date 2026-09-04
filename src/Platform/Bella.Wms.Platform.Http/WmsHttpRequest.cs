using System.Collections.Immutable;

namespace Bella.Wms.Platform.Http;

/// <summary>An outbound request, independent of transport.</summary>
public sealed record WmsHttpRequest
{
    /// <summary>Logical name of the endpoint, used for logging and resilience policy selection.</summary>
    public required string EndpointName { get; init; }

    public required Uri Uri { get; init; }

    public required HttpMethod Method { get; init; }

    /// <summary>
    /// Headers. Replaces the CSV header string <c>headerCurl.sh</c> splits on commas —
    /// a format that breaks on any header value containing a comma.
    /// </summary>
    public ImmutableDictionary<string, string> Headers { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>Serialised request body, or <see langword="null"/>.</summary>
    public string? Body { get; init; }

    public string ContentType { get; init; } = "application/json";

    /// <summary>
    /// Per-request timeout. ABL hard-coded <c>--connect-timeout 10 --max-time 60</c>
    /// in <c>headerCurl.sh</c>; here it is configurable per endpoint.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Idempotency key for retry-safe requests. The Phase 2 rulebook requires an
    /// idempotency strategy per adapter; the ABL had none, which is why a retried
    /// <c>createOrderJob</c> can duplicate work at the far end.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>An outbound response.</summary>
/// <typeparam name="T">Deserialised body type, or <see cref="string"/> for raw.</typeparam>
public sealed record WmsHttpResponse<T>
{
    public required int StatusCode { get; init; }

    public T? Body { get; init; }

    /// <summary>Raw response text, always populated, for the request/response archive.</summary>
    public string RawBody { get; init; } = string.Empty;

    public ImmutableDictionary<string, string> Headers { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    public TimeSpan Duration { get; init; }

    /// <summary>
    /// True for 2xx. Note the ABL routers return HTTP 200 even for application-level
    /// failures — see <c>Bella.Wms.Integration.Partners</c> inbound handling for why
    /// that behaviour is preserved on the inbound side.
    /// </summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
