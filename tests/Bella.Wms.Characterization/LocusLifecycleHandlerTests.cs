using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Pins the exact response strings for the Group 1 handlers (<c>docs/HANDLER_SPECS.md</c>),
/// including the four flagged ABL defects where the job id is parsed and then dropped.
/// </summary>
/// <remarks>
/// These strings are wire-visible and Locus may be logging them, so the assertions are
/// exact-match, not "contains" — a byte drifting here is the regression this guards
/// against.
/// </remarks>
public sealed class LocusLifecycleHandlerTests
{
    private static InboundEventRequest Request(string eventType, string payload) =>
        new(PartnerChannel.Locus, eventType, payload, "J-100");

    [Fact]
    public async Task HoldCompleteIgnoresThePayloadAndDropsTheJobId()
    {
        var handler = new LocusHoldCompleteHandler(NullLogger<LocusHoldCompleteHandler>.Instance);

        // Deliberately malformed JSON: the ABL never parses the payload for this
        // handler (locusAPI.cls:1878-1883 is four lines), so this must not throw.
        var result = await handler.HandleAsync(Request(LocusEventType.HoldComplete, "not json"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult hold complete successful for job id ");
    }

    [Fact]
    public async Task HoldReleaseCompleteAppendsTheJobId()
    {
        var handler = new LocusHoldReleaseCompleteHandler(NullLogger<LocusHoldReleaseCompleteHandler>.Instance);

        var result = await handler.HandleAsync(
            Request(LocusEventType.HoldReleaseComplete, """{"JobId":"J-100"}"""));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult hold release complete successful for job id J-100");
    }

    [Fact]
    public async Task UpdateCompleteParsesTheJobIdAndDropsIt()
    {
        var handler = new LocusUpdateCompleteHandler(NullLogger<LocusUpdateCompleteHandler>.Instance);

        var result = await handler.HandleAsync(
            Request(LocusEventType.UpdateComplete, """{"JobId":"J-100"}"""));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult update complete successful for job id ");
    }

    [Fact]
    public async Task UpdateRejectParsesTheJobIdAndDropsIt()
    {
        var handler = new LocusUpdateRejectHandler(NullLogger<LocusUpdateRejectHandler>.Instance);

        var result = await handler.HandleAsync(
            Request(LocusEventType.UpdateReject, """{"JobId":"J-100"}"""));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult update reject successful for job id ");
    }

    [Fact]
    public async Task CancelCompleteParsesTheJobIdAndDropsIt()
    {
        var handler = new LocusCancelCompleteHandler(NullLogger<LocusCancelCompleteHandler>.Instance);

        var result = await handler.HandleAsync(
            Request(LocusEventType.CancelComplete, """{"JobId":"J-100"}"""));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult cancel complete successful for job id ");
    }
}
