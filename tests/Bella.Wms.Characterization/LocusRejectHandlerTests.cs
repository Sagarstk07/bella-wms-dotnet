using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Characterizes <see cref="LocusRejectHandler"/> — the Group 2 template
/// (<c>docs/HANDLER_SPECS.md</c>) — against its three response paths: carton not found,
/// already processed, and the successful reject.
/// </summary>
public sealed class LocusRejectHandlerTests
{
    private static Carton MakeCarton(string cartonId, string? rowStatus) => new()
    {
        Company = "01",
        Warehouse = "01",
        CartonId = cartonId,
        Order = "ORD-1",
        OrderSuffix = "0",
        RowStatus = rowStatus,
    };

    private static LocusRejectHandler MakeHandler(
        FakeCartonRepository cartons,
        FakeAuditService? audit = null,
        FakeLocusClient? locus = null,
        FakeNotificationService? notifications = null) =>
        new(
            cartons,
            audit ?? new FakeAuditService(),
            locus ?? new FakeLocusClient(),
            notifications ?? new FakeNotificationService(),
            new FakeUnitOfWork(),
            new FakeWmsContext(),
            NullLogger<LocusRejectHandler>.Instance);

    [Fact]
    public async Task CartonNotFoundHoldsTheJobAndReturnsTheAblErrorBody()
    {
        var cartons = new FakeCartonRepository();
        var locus = new FakeLocusClient();
        var handler = MakeHandler(cartons, locus: locus);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.Reject,
                """{"JobId":"J-1","EventInfo":"bad label"}""", "J-1"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("Unable to find carton matching job id J-1");
        result.HandlerSucceeded.Should().BeFalse();
        locus.HoldOrderJobCalls.Should().ContainSingle(c => c.CartonId == "J-1");
    }

    [Theory]
    [InlineData("C")]
    [InlineData("E")]
    public async Task AlreadyProcessedCartonShortCircuitsWithoutWritingAnything(string rowStatus)
    {
        var cartons = new FakeCartonRepository { ["J-2"] = MakeCarton("J-2", rowStatus) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = MakeHandler(cartons, audit: audit, notifications: notifications);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.Reject,
                """{"JobId":"J-2","EventInfo":"bad label"}""", "J-2"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult reject already processed for job id J-2");
        audit.Appended.Should().BeEmpty();
        cartons.MarkErrorCalls.Should().BeEmpty();
        notifications.SentAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task SuccessfulRejectWritesTheJEAuditRowMarksTheCartonAndAlertsByEmail()
    {
        var cartons = new FakeCartonRepository { ["J-3"] = MakeCarton("J-3", rowStatus: null) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = MakeHandler(cartons, audit: audit, notifications: notifications);

        var result = await handler.HandleAsync(
            new InboundEventRequest(
                PartnerChannel.Locus, LocusEventType.Reject,
                """{"JobId":"J-3","EventInfo":"bad label"}""", "J-3"));

        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("OrderJobResult reject successful for job id J-3");
        result.HandlerSucceeded.Should().BeTrue();

        audit.Appended.Should().ContainSingle();
        var row = audit.Appended[0];
        row.TransactionType.Should().Be("JE");
        row.CartonId.Should().Be("J-3");
        row.OrderNumber.Should().Be("ORD-1");
        row.OrderSuffix.Should().Be("0");
        row.Comments.Should().Be("OrderJobResult rejected by Locus: bad label");

        cartons.MarkErrorCalls.Should().ContainSingle(c => c.CartonId == "J-3");

        notifications.SentAlerts.Should().ContainSingle();
        notifications.SentAlerts[0].Subject.Should().Be("Error sending carton ORD-1 - J-3 to locus");
        notifications.SentAlerts[0].Body.Should().Be("OrderJobResult rejected by Locus: bad label");
    }
}
