using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Application.Locus;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Characterizes the three reject-family handlers built on
/// <see cref="LocusRejectFamilyHandlerBase"/> — <c>holdreject</c>,
/// <c>holdreleasereject</c>, <c>cancelreject</c> — that share their shape with
/// <see cref="LocusRejectHandler"/> (covered separately in
/// <c>LocusRejectHandlerTests</c>).
/// </summary>
public sealed class LocusRejectFamilyHandlerTests
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

    private static InboundEventRequest MakeRequest(string eventType, string jobId) =>
        new(
            PartnerChannel.Locus, eventType,
            $$"""{"JobId":"{{jobId}}","EventInfo":"bad label"}""", jobId);

    [Fact]
    public async Task HoldRejectSuccessUsesTheExactStringFromTheSpecTable()
    {
        var cartons = new FakeCartonRepository { ["J-1"] = MakeCarton("J-1", rowStatus: null) };
        var handler = new LocusHoldRejectHandler(
            cartons, new FakeAuditService(), new FakeLocusClient(), new FakeNotificationService(),
            new FakeUnitOfWork(), new FakeWmsContext(), NullLogger<LocusHoldRejectHandler>.Instance);

        var result = await handler.HandleAsync(MakeRequest(LocusEventType.HoldReject, "J-1"));

        result.Body.Should().Be("OrderJobResult hold reject successful for job id J-1");
    }

    [Fact]
    public async Task HoldRejectAuditAndEmailUseTheLiteralAblWording()
    {
        // locusAPI.cls:1938 and 1949. Neither is derivable from reject's wording: the
        // comment drops the "OrderJobResult" prefix, and the subject says "putting … on
        // hold", not "holding". An earlier revision of HANDLER_SPECS.md paraphrased both
        // and both were written wrong.
        var cartons = new FakeCartonRepository { ["J-7"] = MakeCarton("J-7", rowStatus: null) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = new LocusHoldRejectHandler(
            cartons, audit, new FakeLocusClient(), notifications,
            new FakeUnitOfWork(), new FakeWmsContext(), NullLogger<LocusHoldRejectHandler>.Instance);

        await handler.HandleAsync(MakeRequest(LocusEventType.HoldReject, "J-7"));

        audit.Appended.Should().ContainSingle();
        audit.Appended[0].Comments.Should().Be("Hold rejected by Locus: bad label");

        notifications.SentAlerts.Should().ContainSingle();
        notifications.SentAlerts[0].Subject.Should()
            .Be("Error putting carton ORD-1 - J-7 on hold in locus");
        notifications.SentAlerts[0].Body.Should().Be("Hold rejected by Locus: bad label");
    }

    [Fact]
    public async Task HoldRejectCartonNotFoundMatchesTheSharedAblErrorBody()
    {
        var handler = new LocusHoldRejectHandler(
            new FakeCartonRepository(), new FakeAuditService(), new FakeLocusClient(),
            new FakeNotificationService(), new FakeUnitOfWork(), new FakeWmsContext(),
            NullLogger<LocusHoldRejectHandler>.Instance);

        var result = await handler.HandleAsync(MakeRequest(LocusEventType.HoldReject, "J-2"));

        result.Body.Should().Be("Unable to find carton matching job id J-2");
        result.HandlerSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HoldRejectCartonNotFoundDoesNotHoldTheJob()
    {
        // The family's one asymmetry. reject (1832), holdreleasereject (2002) and
        // cancelreject (2124) all call holdOrderJob here; holdreject's call is commented
        // out at locusAPI.cls:1912 — "Removed to prevent hold loops". Holding a job
        // because a hold was rejected asks Locus to hold it again, which fails, which
        // holds it again. This test is what stops someone "tidying" the base class into
        // uniformity.
        var locus = new FakeLocusClient();
        var handler = new LocusHoldRejectHandler(
            new FakeCartonRepository(), new FakeAuditService(), locus,
            new FakeNotificationService(), new FakeUnitOfWork(), new FakeWmsContext(),
            NullLogger<LocusHoldRejectHandler>.Instance);

        await handler.HandleAsync(MakeRequest(LocusEventType.HoldReject, "J-2"));

        locus.HoldOrderJobCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task HoldReleaseRejectSuccessReproducesTheHoldCompleteCopyPasteDefect()
    {
        // ⚠ ABL DEFECT (docs/HANDLER_SPECS.md Group 2, the most consequential of the four
        // string defects): locusAPI.cls:2044 returns holdcomplete's success message, not
        // a hold-release-reject one — reporting success for what is actually a rejection.
        var cartons = new FakeCartonRepository { ["J-3"] = MakeCarton("J-3", rowStatus: null) };
        var handler = new LocusHoldReleaseRejectHandler(
            cartons, new FakeAuditService(), new FakeLocusClient(), new FakeNotificationService(),
            new FakeUnitOfWork(), new FakeWmsContext(),
            NullLogger<LocusHoldReleaseRejectHandler>.Instance);

        var result = await handler.HandleAsync(MakeRequest(LocusEventType.HoldReleaseReject, "J-3"));

        result.Body.Should().Be("OrderJobResult hold complete successful for job id J-3");
    }

    [Fact]
    public async Task HoldReleaseRejectAuditAndEmailUseTheCorrectWordingDespiteTheResponseDefect()
    {
        var cartons = new FakeCartonRepository { ["J-4"] = MakeCarton("J-4", rowStatus: null) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = new LocusHoldReleaseRejectHandler(
            cartons, audit, new FakeLocusClient(), notifications,
            new FakeUnitOfWork(), new FakeWmsContext(),
            NullLogger<LocusHoldReleaseRejectHandler>.Instance);

        await handler.HandleAsync(MakeRequest(LocusEventType.HoldReleaseReject, "J-4"));

        audit.Appended.Should().ContainSingle();
        audit.Appended[0].Comments.Should().Be("Hold release rejected by Locus: bad label");

        notifications.SentAlerts.Should().ContainSingle();
        notifications.SentAlerts[0].Subject.Should()
            .Be("Error releasing carton ORD-1 - J-4 from hold in locus");
    }

    [Fact]
    public async Task CancelRejectSuccessUsesTheExactCancelOrderStringFromTheSpecTable()
    {
        // Notably not "cancel reject successful" — the ABL gives this one as
        // "cancel order successful" (2166), unlike the other three handlers in the family.
        var cartons = new FakeCartonRepository { ["J-5"] = MakeCarton("J-5", rowStatus: null) };
        var handler = new LocusCancelRejectHandler(
            cartons, new FakeAuditService(), new FakeLocusClient(), new FakeNotificationService(),
            new FakeUnitOfWork(), new FakeWmsContext(), NullLogger<LocusCancelRejectHandler>.Instance);

        var result = await handler.HandleAsync(MakeRequest(LocusEventType.CancelReject, "J-5"));

        result.Body.Should().Be("OrderJobResult cancel order successful for job id J-5");
    }

    [Theory]
    [InlineData("C")]
    [InlineData("E")]
    public async Task CancelRejectAlreadyProcessedShortCircuitsWithoutWritingAnything(string rowStatus)
    {
        var cartons = new FakeCartonRepository { ["J-6"] = MakeCarton("J-6", rowStatus) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = new LocusCancelRejectHandler(
            cartons, audit, new FakeLocusClient(), notifications,
            new FakeUnitOfWork(), new FakeWmsContext(), NullLogger<LocusCancelRejectHandler>.Instance);

        var result = await handler.HandleAsync(MakeRequest(LocusEventType.CancelReject, "J-6"));

        // locusAPI.cls:2132 — "cancel reject", not "cancel order". This method uses a
        // different noun for the same event than its own success string at 2166 does.
        result.Body.Should().Be("OrderJobResult cancel reject already processed for job id J-6");
        audit.Appended.Should().BeEmpty();
        cartons.MarkErrorCalls.Should().BeEmpty();
        notifications.SentAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelRejectEmailBodyReproducesTheCnacelTypo()
    {
        // ⚠ ABL DEFECT: locusAPI.cls:2162 spells it "Cnacel". It is the only handler in
        // the family whose email body differs from its audit comment, and it differs only
        // by the typo. Reproduced deliberately — the string lands in operators' alert
        // mailboxes and a mail rule may match on it.
        var cartons = new FakeCartonRepository { ["J-8"] = MakeCarton("J-8", rowStatus: null) };
        var audit = new FakeAuditService();
        var notifications = new FakeNotificationService();
        var handler = new LocusCancelRejectHandler(
            cartons, audit, new FakeLocusClient(), notifications,
            new FakeUnitOfWork(), new FakeWmsContext(), NullLogger<LocusCancelRejectHandler>.Instance);

        await handler.HandleAsync(MakeRequest(LocusEventType.CancelReject, "J-8"));

        audit.Appended[0].Comments.Should().Be("Cancel order rejected by Locus: bad label");

        notifications.SentAlerts[0].Subject.Should()
            .Be("Error cancelling carton ORD-1 - J-8 in locus");
        notifications.SentAlerts[0].Body.Should().Be("Cnacel rejected by Locus: bad label");
    }
}
