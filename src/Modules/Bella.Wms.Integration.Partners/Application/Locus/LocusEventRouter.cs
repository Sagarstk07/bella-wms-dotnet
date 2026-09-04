using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Http;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// Resolves the event name and correlation key from a Locus payload, then dispatches.
/// </summary>
/// <remarks>
/// Conversion of the <c>router</c> internal procedure in <c>api/wms/wsLocusAPI.p</c>
/// (lines 59-315). Authentication and session setup move to the host; what remains here
/// is payload shape detection and the licence-plate prefix rule.
/// </remarks>
public sealed class LocusEventRouter
{
    private readonly InboundEventRegistry _registry;
    private readonly IRequestArchive _archive;
    private readonly IWmsContext _context;
    private readonly ILogger<LocusEventRouter> _logger;

    public LocusEventRouter(
        InboundEventRegistry registry,
        IRequestArchive archive,
        IWmsContext context,
        ILogger<LocusEventRouter> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InboundEventResult> RouteAsync(
        string payload,
        CancellationToken cancellationToken = default)
    {
        LocusRoute route;

        try
        {
            route = ResolveRoute(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Locus payload could not be parsed.");
            return InboundEventResult.Rejected(400, "Unrecognized JSON payload");
        }

        var eventType = route.EventType;

        if (eventType is null)
        {
            // ABL: falls through all three branches without setting sHttpStatus,
            // returning an empty body. Made explicit here.
            _logger.LogWarning("Locus payload matched no known wrapper object.");
            return InboundEventResult.Rejected(400, "Unrecognized JSON payload");
        }

        // ABL wsLocusAPI.p:167 archives the request before dispatch.
        await _archive.RecordInboundAsync(
            PartnerChannel.Locus, eventType, route.CorrelationKey,
            payload, _context, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Locus router before - {EventType} {CorrelationKey}",
            eventType, route.CorrelationKey);

        InboundEventResult result;

        if (_registry.TryResolve(PartnerChannel.Locus, eventType, out var handler))
        {
            result = await handler.HandleAsync(
                new InboundEventRequest(
                    PartnerChannel.Locus, eventType, route.InnerPayload, route.CorrelationKey),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = InboundEventRegistry.UnknownEventResult(eventType);
        }

        _logger.LogInformation(
            "Locus router after - {EventType} | {Body}", eventType, result.Body);

        // ABL wsLocusAPI.p:187 archives the response after dispatch.
        await _archive.RecordInboundResponseAsync(
            PartnerChannel.Locus, eventType, route.CorrelationKey,
            result.Body, _context, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Reproduces the three-branch payload detection in <c>wsLocusAPI.p:131-300</c>.
    /// </summary>
    private static LocusRoute ResolveRoute(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // Branch 1 — OrderJobResult (line 131). Event name taken verbatim.
        if (root.TryGetProperty("OrderJobResult", out var orderJobResult))
        {
            return new LocusRoute(
                EventType: GetString(orderJobResult, "EventType"),
                CorrelationKey: GetString(orderJobResult, "JobId") ?? "unknown",
                InnerPayload: orderJobResult.GetRawText());
        }

        // Branch 2 — PutawayJobRequest (line 195). Event name is a constant.
        if (root.TryGetProperty("PutawayJobRequest", out var putawayRequest))
        {
            return new LocusRoute(
                EventType: LocusEventType.PutawayRequest,
                CorrelationKey: GetString(putawayRequest, "LicensePlate") ?? "unknown",
                InnerPayload: putawayRequest.GetRawText());
        }

        // Branch 3 — PutawayJobResult (line 237). Event name is PREFIXED.
        if (root.TryGetProperty("PutawayJobResult", out var putawayResult))
        {
            var rawEvent = GetString(putawayResult, "EventType");
            var licencePlate = GetString(putawayResult, "LicensePlate") ?? string.Empty;

            return new LocusRoute(
                EventType: rawEvent is null ? null : ApplyLicencePlatePrefix(rawEvent, licencePlate),
                CorrelationKey: licencePlate.Length == 0 ? "unknown" : licencePlate,
                InnerPayload: putawayResult.GetRawText());
        }

        return new LocusRoute(null, "unknown", payload);
    }

    /// <summary>
    /// The licence-plate prefix rule from <c>wsLocusAPI.p:259-265</c>.
    /// </summary>
    /// <remarks>
    /// <para>The ABL is:</para>
    /// <code>
    /// ASSIGN isNumeric = SUBSTRING(cSSCC18, 1, 1).
    /// ASSIGN tempNum = INT(isNumeric) NO-ERROR.
    /// IF ERROR-STATUS:ERROR THEN   // starts with a letter, it's a toteID
    ///      cAction = "putaway" + cAction.
    /// ELSE
    ///      cAction = "replen" + cAction.
    /// </code>
    /// <para>
    /// So: first character parses as an integer means replenishment, otherwise putaway.
    /// An empty licence plate makes <c>SUBSTRING</c> return <c>""</c>, <c>INT("")</c>
    /// raises an error, and the event becomes a <c>putaway</c> one. That edge case is
    /// preserved rather than treated as invalid input.
    /// </para>
    /// </remarks>
    internal static string ApplyLicencePlatePrefix(string eventType, string licencePlate) =>
        LocusLicencePlateRule.FirstCharacterIsNumeric(licencePlate)
            ? $"REPLEN{eventType}"
            : $"PUTAWAY{eventType}";

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct LocusRoute(
        string? EventType,
        string CorrelationKey,
        string InnerPayload);
}
