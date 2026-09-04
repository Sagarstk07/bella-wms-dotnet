using Bella.Wms.Integration.Partners.Contracts;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// The explicit route table that replaces <c>dynamic-invoke</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every inbound event must be registered here to be reachable. That is the whole point:
/// in the ABL, an event name arriving from outside could reach any public method on the
/// target class, including helpers like <c>parseJSOProperty</c>
/// (<c>locusAPI.cls:4097</c>) and operations never intended as endpoints like
/// <c>releaseReplenishment</c> (<c>locusAPI.cls:4134</c>) and <c>testtotemove</c>
/// (<c>locusAPI.cls:3188</c>).
/// </para>
/// <para>
/// <b>Unknown events behave as the ABL does.</b> <c>wsLocusAPI.p:177-181</c> catches the
/// reflection failure and returns HTTP 200 with
/// <c>"Invalid Endpoint Action " + cAction</c>. That exact shape is reproduced by
/// <see cref="UnknownEventResult"/>, because changing it to a 404 changes what Locus and
/// the AutoBagger middleware see, and their retry logic may depend on the current
/// behaviour. Confirm against captured traffic before altering it.
/// </para>
/// </remarks>
public sealed class InboundEventRegistry
{
    private readonly Dictionary<string, IInboundEventHandler> _handlers;
    private readonly ILogger<InboundEventRegistry> _logger;

    public InboundEventRegistry(
        IEnumerable<IInboundEventHandler> handlers,
        ILogger<InboundEventRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _handlers = new Dictionary<string, IInboundEventHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            var key = Key(handler.Channel, handler.EventType);

            if (!_handlers.TryAdd(key, handler))
            {
                throw new InvalidOperationException(
                    $"Two handlers are registered for {key}: " +
                    $"{_handlers[key].GetType().Name} and {handler.GetType().Name}.");
            }
        }
    }

    /// <summary>
    /// Every registered route, for the startup log and for the contract test that asserts
    /// the registered set matches the documented API surface.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredRoutes => _handlers.Keys;

    /// <summary>Resolves a handler, or <see langword="false"/> if the event is unknown.</summary>
    public bool TryResolve(string channel, string eventType, out IInboundEventHandler handler)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            // ABL wsMiddlewareAPI.p:295 — "Missing EventType In Payload".
            _logger.LogWarning("Inbound request on {Channel} carried no event type.", channel);
            handler = null!;
            return false;
        }

        if (_handlers.TryGetValue(Key(channel, eventType), out var found))
        {
            handler = found;
            return true;
        }

        _logger.LogWarning(
            "Unregistered inbound event {EventType} on channel {Channel}.",
            eventType, channel);

        handler = null!;
        return false;
    }

    /// <summary>
    /// The response for an unregistered event, byte-identical to the ABL's.
    /// <c>wsLocusAPI.p:179-180</c>.
    /// </summary>
    public static InboundEventResult UnknownEventResult(string eventType) =>
        InboundEventResult.ApplicationFailure($"Invalid Endpoint Action {eventType}");

    private static string Key(string channel, string eventType) => $"{channel}:{eventType}";
}
