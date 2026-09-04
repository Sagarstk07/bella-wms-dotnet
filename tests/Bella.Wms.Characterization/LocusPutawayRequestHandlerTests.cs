using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Characterizes <see cref="LocusPutawayRequestHandler"/> and
/// <see cref="LocusPutawayAcceptHandler"/> — the two Group 5 handlers
/// (<c>docs/HANDLER_SPECS.md</c>).
/// </summary>
public sealed class LocusPutawayRequestHandlerTests
{
    [Fact]
    public async Task NumericFirstCharacterIsTreatedAsAnSscc18AndCallsCreatePutawayJob()
    {
        var locus = new FakeLocusClient();
        var handler = new LocusPutawayRequestHandler(locus, NullLogger<LocusPutawayRequestHandler>.Instance);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.PutawayRequest,
                """{"LicensePlate":"9876543210","RequestRobot":"ROBOT-1"}""", "9876543210"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("PutawayJobRequest for SSCC18 9876543210 ");

        locus.CreatePutawayJobCalls.Should().ContainSingle(c => c.Sscc18 == "9876543210" && c.Robot == "ROBOT-1");
        locus.CreatePutawayJobForToteCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task NonNumericFirstCharacterIsTreatedAsAToteButTheResponseStillSaysSscc18()
    {
        var locus = new FakeLocusClient();
        var handler = new LocusPutawayRequestHandler(locus, NullLogger<LocusPutawayRequestHandler>.Instance);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.PutawayRequest,
                """{"LicensePlate":"TOTE123","RequestRobot":"ROBOT-1"}""", "TOTE123"));

        result.StatusCode.Should().Be(200);
        // Says "SSCC18" even on the tote branch — reproduced exactly per the spec.
        result.Body.Should().Be("PutawayJobRequest for SSCC18 TOTE123 ");

        locus.CreatePutawayJobForToteCalls.Should().ContainSingle(c => c.ToteId == "TOTE123" && c.Robot == "ROBOT-1");
        locus.CreatePutawayJobCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyLicensePlateIsTreatedAsAToteSameAsTheRouterRule()
    {
        var locus = new FakeLocusClient();
        var handler = new LocusPutawayRequestHandler(locus, NullLogger<LocusPutawayRequestHandler>.Instance);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.PutawayRequest,
                """{"LicensePlate":"","RequestRobot":"ROBOT-1"}""", ""));

        result.Body.Should().Be("PutawayJobRequest for SSCC18  ");
        locus.CreatePutawayJobForToteCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task PutawayAcceptIgnoresThePayloadAndReturnsTheFixedString()
    {
        var handler = new LocusPutawayAcceptHandler(NullLogger<LocusPutawayAcceptHandler>.Instance);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.PutawayAccept, "not json", "unused"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("PutawayJobResult accept successful");
    }
}
