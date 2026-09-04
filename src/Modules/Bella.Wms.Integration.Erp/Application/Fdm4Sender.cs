using System.Collections.Immutable;
using System.Text;
using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>Configuration for the FDM4 ERP endpoint. Mirrors <c>interface/config.json</c>.</summary>
/// <remarks>
/// <para>
/// <b>Credentials do not live here.</b> The ABL <c>config.json</c> holds
/// <c>"apiKey": "OJsLL7IuTp"</c> in plain text in source control, and the same key is
/// compiled into <c>wsLocusAPI.p:28</c>. In .NET the key is supplied by the configuration
/// provider chain — environment, user secrets or a vault — and never committed.
/// The property below is bound, not defaulted.
/// </para>
/// </remarks>
public sealed class Fdm4Options
{
    public const string SectionName = "Fdm4";

    /// <summary>ABL <c>apiEndPoint</c>.</summary>
    /// <remarks>
    /// Not marked <c>required</c>: a type with required members cannot satisfy the
    /// <c>new()</c> constraint on <c>IServiceCollection.Configure&lt;TOptions&gt;</c>
    /// (CS9040). Absence is enforced by <see cref="Validate"/> at startup instead, which
    /// gives a better error than a binder failure anyway.
    /// </remarks>
    public Uri? Endpoint { get; init; }

    /// <summary>
    /// ABL <c>apiKey</c>. Supplied from configuration at runtime — never a literal,
    /// never committed.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>ABL <c>RETRY_COUNT</c>.</summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>ABL <c>RETRY_SECONDS_PAUSE</c>.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Fails fast at startup when the endpoint or key is missing, rather than on the
    /// first message. Call from the host after binding.
    /// </summary>
    public void Validate()
    {
        if (Endpoint is null)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Endpoint is not configured. It is the FDM4 apiEndPoint " +
                "from the ABL interface/config.json.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"{SectionName}:ApiKey is not configured. Supply it from environment, " +
                "user secrets or a vault — never a source literal.");
        }
    }
}

/// <summary>
/// Sends payloads to FDM4 over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Conversion of <c>api/wms/interface/tofdm4.cls</c>. Two defects in the ABL were found
/// during conversion and are <b>fixed here</b> rather than reproduced. Both are recorded
/// in <c>docs/ABL_CROSSREF.md</c> and both need confirming against captured traffic
/// before this goes live, because FDM4 has been receiving the broken form for as long as
/// this code has run.
/// </para>
/// <para>
/// <b>Defect 1 — the Accept header is never sent.</b> <c>tofdm4.cls:227-229</c> builds a
/// comma-separated header string:
/// </para>
/// <code>
/// cHeaderParams = "AUTHENTICATION: basic " + ...
/// cHeaderParams = cHeaderParams + ",Content-Type: application/json":U.
/// cHeaderParams = cHeaderParams + "Accept: application/json":U.   // no leading comma
/// </code>
/// <para>
/// The third line has no leading comma, so <c>headerCurl.sh</c> — which splits that
/// string on commas — sees one header whose value is
/// <c>"application/jsonAccept: application/json"</c>. FDM4 has never received a valid
/// <c>Content-Type</c> or any <c>Accept</c> header from this path. Sending both correctly
/// is a wire-visible change.
/// </para>
/// <para>
/// <b>Defect 2 — half the configured retries happen.</b> <c>tofdm4.cls:234</c> opens
/// <c>do iRetry = 1 to {&amp;RETRY_COUNT}</c>, and line 247 then does
/// <c>iRetry = iRetry + 1</c> inside the body. ABL increments the loop counter itself,
/// so the manual increment doubles the step and the loop runs
/// <c>ceil(RETRY_COUNT / 2)</c> times. The retry count here is honoured as written.
/// </para>
/// </remarks>
public sealed class Fdm4Sender : IFdm4Sender
{
    private readonly IWmsHttpClient _httpClient;
    private readonly IBusinessRuleService _businessRules;
    private readonly IWmsContext _context;
    private readonly Fdm4Options _options;
    private readonly ILogger<Fdm4Sender> _logger;

    public Fdm4Sender(
        IWmsHttpClient httpClient,
        IBusinessRuleService businessRules,
        IWmsContext context,
        IOptions<Fdm4Options> options,
        ILogger<Fdm4Sender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _businessRules = businessRules ?? throw new ArgumentNullException(nameof(businessRules));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<Fdm4Response>> SendAsync(
        string payload,
        string requestIdentifier,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        // ABL tofdm4.cls:130 gates on business rule 7510 per warehouse. A warehouse
        // that has not opted into API mode still uses the file-based ifaces/ path, so
        // sending here would duplicate the message.
        var enabled = await _businessRules
            .IsEnabledAsync(BusinessRule.InterfaceApiEnabled, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!enabled)
        {
            return OperationResult<Fdm4Response>.Failure(
                $"Interface API (business rule {BusinessRule.InterfaceApiEnabled}) is not " +
                $"enabled for {_context.Company}/{_context.Warehouse}.",
                "INTERFACE_API_DISABLED");
        }

        // ABL: "AUTHENTICATION: basic " + base64(apiKey).
        // The header name and the base64 encoding are preserved exactly — FDM4 is
        // matching on them, so this is contract, not style.
        var encodedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(_options.ApiKey));

        var headers = ImmutableDictionary<string, string>.Empty
            .Add("AUTHENTICATION", $"basic {encodedKey}")
            .Add("Accept", "application/json");

        _options.Validate();

        var request = new WmsHttpRequest
        {
            EndpointName = "fdm4",
            Uri = _options.Endpoint!,
            Method = HttpMethod.Post,
            Headers = headers,
            Body = payload,
            ContentType = "application/json",
            Timeout = _options.Timeout,
            IdempotencyKey = fileName,
        };

        for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
        {
            if (attempt > 1)
            {
                _logger.LogWarning(
                    "Retrying {RequestIdentifier} for {Company}/{Warehouse}, attempt {Attempt} of {Total}.",
                    requestIdentifier, _context.Company, _context.Warehouse, attempt, _options.RetryCount);

                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            var result = await _httpClient.SendRawAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Failed || result.Value is null)
            {
                _logger.LogError(
                    "{RequestIdentifier} attempt {Attempt} failed: {Message}",
                    requestIdentifier, attempt, result.Message);
                continue;
            }

            var response = result.Value;

            // ABL retries on server error (5xx) and not on 4xx — a 4xx means the payload
            // is wrong and resending it will not help.
            if (response.StatusCode >= 500)
            {
                _logger.LogError(
                    "{RequestIdentifier} attempt {Attempt} returned server error {Status}.",
                    requestIdentifier, attempt, response.StatusCode);
                continue;
            }

            if (!response.IsSuccess)
            {
                return OperationResult<Fdm4Response>.Failure(
                    $"FDM4 returned {response.StatusCode} for {requestIdentifier}: {response.RawBody}",
                    "FDM4_REJECTED");
            }

            return OperationResult<Fdm4Response>.Success(
                new Fdm4Response(response.StatusCode, response.RawBody, ExtractTransmissionId(response.RawBody)));
        }

        return OperationResult<Fdm4Response>.Failure(
            $"FDM4 send failed after {_options.RetryCount} attempts for {requestIdentifier}.",
            "FDM4_RETRIES_EXHAUSTED");
    }

    /// <summary>
    /// Pulls the transmission id out of the FDM4 response.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> until the response contract is confirmed against
    /// captured traffic. <c>tofdm4.cls</c> parses the response body for this, but the
    /// exact property name has not been characterised — see <c>docs/SCHEMA_REVIEW.md</c>
    /// open item 6.
    /// </remarks>
    private static string? ExtractTransmissionId(string body) => null;
}
