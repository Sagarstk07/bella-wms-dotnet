using Bella.Wms.Integration.Erp.Contracts;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Erp.Application;

/// <summary>
/// Resolves the processor for an endpoint and event type.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <c>api/wms/interface/WMSCommOutProcess.cls</c> and
/// <c>WMSCommInProcess.cls</c>, whose <c>GetProcessor</c> methods are nested
/// <c>CASE</c> statements returning <c>?</c> (unknown) for anything unregistered.
/// </para>
/// <para>
/// Two improvements over the ABL, both deliberate:
/// </para>
/// <list type="number">
///   <item>
///     Registration lives on the processor (<see cref="ICommOutProcessor.EventType"/>)
///     rather than in a switch a developer must remember to extend. Adding a processor
///     is a DI registration, not an edit to a shared file.
///   </item>
///   <item>
///     A duplicate registration throws at startup instead of silently letting one
///     processor shadow another. The ABL <c>CASE</c> would take the first matching
///     branch and never report the collision.
///   </item>
/// </list>
/// <para>
/// The ABL returns <c>?</c> for an unregistered type and logs; the caller then treats it
/// as an error. That behaviour is preserved — <see cref="TryResolveOut"/> returns
/// <see langword="false"/> rather than throwing, because an unroutable message must move
/// to <c>E</c> status rather than crash the poller.
/// </para>
/// </remarks>
public sealed class CommProcessorRegistry
{
    private readonly Dictionary<string, ICommOutProcessor> _outProcessors;
    private readonly Dictionary<string, ICommInProcessor> _inProcessors;
    private readonly ILogger<CommProcessorRegistry> _logger;

    public CommProcessorRegistry(
        IEnumerable<ICommOutProcessor> outProcessors,
        IEnumerable<ICommInProcessor> inProcessors,
        ILogger<CommProcessorRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(outProcessors);
        ArgumentNullException.ThrowIfNull(inProcessors);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _outProcessors = new Dictionary<string, ICommOutProcessor>(StringComparer.OrdinalIgnoreCase);
        foreach (var processor in outProcessors)
        {
            var key = Key(processor.Endpoint, processor.EventType);
            if (!_outProcessors.TryAdd(key, processor))
            {
                throw new InvalidOperationException(
                    $"Two outbound processors are registered for {key}: " +
                    $"{_outProcessors[key].GetType().Name} and {processor.GetType().Name}.");
            }
        }

        _inProcessors = new Dictionary<string, ICommInProcessor>(StringComparer.OrdinalIgnoreCase);
        foreach (var processor in inProcessors)
        {
            var key = Key(processor.Endpoint, processor.EventType);
            if (!_inProcessors.TryAdd(key, processor))
            {
                throw new InvalidOperationException(
                    $"Two inbound processors are registered for {key}: " +
                    $"{_inProcessors[key].GetType().Name} and {processor.GetType().Name}.");
            }
        }
    }

    /// <summary>Every registered outbound route, for startup logging and diagnostics.</summary>
    public IReadOnlyCollection<string> RegisteredOutboundRoutes => _outProcessors.Keys;

    /// <summary>Every registered inbound route.</summary>
    public IReadOnlyCollection<string> RegisteredInboundRoutes => _inProcessors.Keys;

    /// <summary>
    /// Equivalent of <c>WMSCommOutProcess:GetProcessor(endpoint, eventType)</c>.
    /// Returns <see langword="false"/> where the ABL returned <c>?</c>.
    /// </summary>
    public bool TryResolveOut(string endpoint, string eventType, out ICommOutProcessor processor)
    {
        if (_outProcessors.TryGetValue(Key(endpoint, eventType), out var found))
        {
            processor = found;
            return true;
        }

        // Mirrors writeToLog("GetProcessor: no processor registered for ...").
        _logger.LogError(
            "No outbound processor registered for endpoint {Endpoint} event {EventType}.",
            endpoint, eventType);

        processor = null!;
        return false;
    }

    /// <summary>Equivalent of <c>WMSCommInProcess:GetProcessor(endpoint, eventType)</c>.</summary>
    public bool TryResolveIn(string endpoint, string eventType, out ICommInProcessor processor)
    {
        if (_inProcessors.TryGetValue(Key(endpoint, eventType), out var found))
        {
            processor = found;
            return true;
        }

        _logger.LogError(
            "No inbound processor registered for endpoint {Endpoint} event {EventType}.",
            endpoint, eventType);

        processor = null!;
        return false;
    }

    private static string Key(string endpoint, string eventType) =>
        $"{endpoint}:{eventType}";
}
