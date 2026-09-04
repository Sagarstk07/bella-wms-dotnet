using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Infrastructure;
using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Integration.Partners.Infrastructure;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Bella.Wms.Platform.Http;
using Bella.Wms.Platform.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Asserts the dependency graph actually resolves.
/// </summary>
/// <remarks>
/// <para>
/// The first draft of this solution had six wiring gaps — a missing implementation, a
/// service that could not be constructed by DI, two unregistered repositories, a module
/// that was never registered, and a cache with the wrong lifetime. Every one of them
/// would have thrown at startup rather than at compile time.
/// </para>
/// <para>
/// This test is the guard against that class of mistake. It builds the real container
/// from the real registration extensions and resolves every root, with
/// <c>ValidateOnBuild</c> and <c>ValidateScopes</c> on so captive dependencies fail here
/// rather than in production.
/// </para>
/// </remarks>
public sealed class CompositionTests
{
    /// <remarks>
    /// Callers must tear this down with <c>CreateAsyncScope</c> / <c>await using</c>.
    /// <see cref="IUnitOfWork"/> is <see cref="IAsyncDisposable"/> only — a deliberate
    /// choice for a type that owns a database connection — and MS.DI throws on a
    /// synchronous scope dispose once a scoped async-only disposable has been resolved.
    /// </remarks>
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        // The units of work validate their connection string in the constructor, so these
        // must be non-empty. They point nowhere and nothing in this test opens a
        // connection — resolution is what is under test, not I/O.
        services.AddSingleton(Options.Create(new OpenEdgeOptions
        {
            IrmsConnectionString = "Driver={Progress OpenEdge};HOST=none;DB=irms",
            WmsCommConnectionString = "Driver={Progress OpenEdge};HOST=none;DB=wmscomm",
        }));

        services.AddSingleton(Options.Create(new RequestArchiveOptions
        {
            Enabled = false,
        }));

        services.AddSingleton(Options.Create(new Fdm4Options
        {
            Endpoint = new Uri("https://fdm4.invalid/router"),
            ApiKey = "test-only-not-a-real-key",
        }));

        services.AddWmsPlatform();
        services.AddWmsConfig();
        services.AddWmsAudit();
        services.AddWmsHttp();

        services.AddPartnersModule();
        services.AddPartnersInfrastructure();
        services.AddErpModule();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            // Catches a singleton capturing a scoped dependency — the exact failure mode
            // that made wms_webhost/wmswipeout.p necessary in the ABL.
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    /// <summary>
    /// Every service the hosts resolve directly must be constructible.
    /// </summary>
    /// <remarks>
    /// Registration alone proves nothing — a registered type whose constructor asks for
    /// something unregistered fails only when someone resolves it.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IWmsContext))]
    [InlineData(typeof(WmsContext))]
    [InlineData(typeof(IEmployeeLookup))]
    [InlineData(typeof(IBusinessRuleService))]
    [InlineData(typeof(IAuditService))]
    [InlineData(typeof(IWmsHttpClient))]
    [InlineData(typeof(IRequestArchive))]
    [InlineData(typeof(RequestScopedRuleCache))]
    [InlineData(typeof(InboundEventRegistry))]
    [InlineData(typeof(LocusEventRouter))]
    [InlineData(typeof(CommProcessorRegistry))]
    [InlineData(typeof(CommOutRouter))]
    public async Task EveryHostRootResolves(Type serviceType)
    {
        await using var provider = BuildContainer();
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        resolved.Should().NotBeNull(
            $"{serviceType.Name} is resolved by a host and every dependency must be registered");
    }

    /// <summary>
    /// The registered inbound surface must match the documented Locus event list exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that keeps <c>dynamic-invoke</c> from creeping back. In the ABL,
    /// adding a public method to <c>locusAPI.cls</c> silently added an endpoint, because
    /// reflection dispatch made every public method reachable from the wire. Here, an
    /// endpoint exists only if it is on this list.
    /// </para>
    /// <para>
    /// <c>testtotemove</c> (<c>locusAPI.cls:3188</c>) is deliberately absent — a test hook
    /// that reflection made reachable from production traffic.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RegisteredLocusEventsMatchTheDocumentedSurface()
    {
        await using var provider = BuildContainer();
        await using var scope = provider.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<InboundEventRegistry>();

        var registered = registry.RegisteredRoutes
            .Where(r => r.StartsWith(PartnerChannel.Locus + ":", StringComparison.OrdinalIgnoreCase))
            .Select(r => r[(PartnerChannel.Locus.Length + 1)..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] expected =
        [
            LocusEventType.Accept,
            LocusEventType.Reject,
            LocusEventType.HoldComplete,
            LocusEventType.HoldReject,
            LocusEventType.HoldReleaseComplete,
            LocusEventType.HoldReleaseReject,
            LocusEventType.UpdateComplete,
            LocusEventType.UpdateReject,
            LocusEventType.CancelComplete,
            LocusEventType.CancelReject,
            LocusEventType.ToteInduct,
            LocusEventType.ToteMove,
            LocusEventType.ToteInductCancel,
            LocusEventType.PutawayRequest,
            LocusEventType.PutawayAccept,
            LocusEventType.PutawayReject,
            LocusEventType.PutawayPutInduct,
            LocusEventType.PutawayPut,
            LocusEventType.PutawayPutComplete,
            LocusEventType.ReplenPutComplete,
            LocusEventType.Pick,
            LocusEventType.PickComplete,
        ];

        registered.Should().BeEquivalentTo(
            expected,
            "the inbound API surface is the registration list, not whatever methods happen to be public");

        registered.Should().NotContain(
            LocusEventType.TestToteMove,
            "testtotemove is a test hook and must not be reachable from the wire");
    }

    /// <summary>
    /// The five outbound ERP event types from <c>WMSCommOutProcess.cls</c> must all route.
    /// </summary>
    [Theory]
    [InlineData("IRCODT")]
    [InlineData("IRSHUP")]
    [InlineData("IRPMUP")]
    [InlineData("IRPIUP")]
    [InlineData("IRORUP")]
    public async Task EveryAblRegisteredErpEventTypeResolves(string eventType)
    {
        await using var provider = BuildContainer();
        await using var scope = provider.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<CommProcessorRegistry>();

        registry.TryResolveOut("OMS", eventType, out _)
            .Should()
            .BeTrue($"WMSCommOutProcess.cls registers {eventType} and the conversion must too");
    }

    /// <summary>
    /// An unregistered event type must not resolve, and must not throw — the ABL logs and
    /// returns unknown so the caller can move the message to error status rather than
    /// killing the poller.
    /// </summary>
    [Fact]
    public async Task UnregisteredErpEventTypeDoesNotResolveAndDoesNotThrow()
    {
        await using var provider = BuildContainer();
        await using var scope = provider.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<CommProcessorRegistry>();

        registry.TryResolveOut("OMS", "NOSUCHTYPE", out _).Should().BeFalse();
        registry.TryResolveOut("NOSUCHENDPOINT", "IRCODT", out _).Should().BeFalse();
    }
}
