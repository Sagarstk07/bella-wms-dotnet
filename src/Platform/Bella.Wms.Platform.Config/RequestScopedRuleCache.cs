namespace Bella.Wms.Platform.Config;

/// <summary>
/// A cache whose lifetime is exactly one request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <c>IMemoryCache</c>.</b> Phase 6 §9 is specific about
/// <c>syspar_value</c>:
/// </para>
/// <para>
/// <i>"Do not cache it in .NET beyond a request without an invalidation path, and never
/// fork it into a .NET-side config file — a divergence there produces two systems that
/// disagree about the rules while both believing they are right."</i>
/// </para>
/// <para>
/// <c>IMemoryCache</c> is registered as a singleton by convention. Injecting it into a
/// scoped service and calling the result "request-scoped" relies on nobody ever
/// registering it the usual way — a guarantee held by a comment rather than by the type
/// system. The first draft of <c>BusinessRuleService</c> made exactly that mistake.
/// </para>
/// <para>
/// This type cannot outlive its scope, so the guarantee is structural. It is deliberately
/// tiny: no eviction, no expiry, no size limit, because a single request cannot read
/// enough configuration for any of those to matter.
/// </para>
/// <para>
/// During parallel run both stacks read the same rows from the same table. A rule changed
/// mid-request is seen by the next request, which is the same behaviour the ABL has —
/// <c>static_connect:getRule</c> reads live every time.
/// </para>
/// </remarks>
public sealed class RequestScopedRuleCache
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    /// <summary>Number of entries cached in this request. Useful in diagnostics.</summary>
    public int Count => _values.Count;

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or calls
    /// <paramref name="factory"/> once and caches what it returns — including
    /// <see langword="null"/>, so an unset rule is not re-queried on every read.
    /// </summary>
    public async Task<string?> GetOrAddAsync(string key, Func<Task<string?>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_values.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var value = await factory().ConfigureAwait(false);
        _values[key] = value;
        return value;
    }
}
