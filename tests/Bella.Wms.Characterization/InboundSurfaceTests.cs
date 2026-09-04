using Bella.Wms.Integration.Partners.Application.Locus;
using FluentAssertions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Characterization tests for the inbound routing rules.
/// </summary>
/// <remarks>
/// <para>
/// Phase 6 §10 item 7: "If the acceptance suite is not running in CI before the first
/// module is written, it will not be running when the first module is finished."
/// </para>
/// <para>
/// These assert behaviour taken from reading the ABL. They are the starting point, not
/// the suite — the real acceptance criterion is the Phase 5 <c>chartest</c> harness
/// replaying captured traffic, and that harness has not been switched on yet
/// (Phase 6 §12 item 4).
/// </para>
/// </remarks>
public sealed class LicencePlatePrefixTests
{
    /// <summary>
    /// The rule from <c>wsLocusAPI.p:259-265</c>: a licence plate whose first character
    /// parses as an integer is a replenishment; anything else is a putaway.
    /// </summary>
    [Theory]
    [InlineData("1234567890", "PUT", "REPLENPUT")]
    [InlineData("0ABCDEF", "PUT", "REPLENPUT")]
    [InlineData("9", "PUTCOMPLETE", "REPLENPUTCOMPLETE")]
    [InlineData("TOTE123", "PUT", "PUTAWAYPUT")]
    [InlineData("A1234", "ACCEPT", "PUTAWAYACCEPT")]
    [InlineData("tote99", "PUT", "PUTAWAYPUT")]
    public void PrefixIsChosenByFirstCharacterOfLicencePlate(
        string licencePlate, string eventType, string expected)
    {
        LocusEventRouter
            .ApplyLicencePlatePrefix(eventType, licencePlate)
            .Should()
            .Be(expected);
    }

    /// <summary>
    /// An empty licence plate: ABL <c>SUBSTRING("", 1, 1)</c> yields <c>""</c>,
    /// <c>INT("")</c> raises an error, and the ELSE branch is not taken — so it becomes a
    /// putaway. Preserved rather than treated as invalid input, because changing it would
    /// silently reroute a malformed payload that currently has defined behaviour.
    /// </summary>
    [Fact]
    public void EmptyLicencePlateIsTreatedAsPutaway()
    {
        LocusEventRouter
            .ApplyLicencePlatePrefix("PUT", string.Empty)
            .Should()
            .Be("PUTAWAYPUT");
    }
}

/// <summary>
/// Asserts the unknown-event response is byte-identical to the ABL's.
/// </summary>
/// <remarks>
/// <c>wsLocusAPI.p:177-181</c> returns HTTP 200 with
/// <c>"Invalid Endpoint Action " + cAction</c> when reflection dispatch fails. Locus has
/// been receiving that for years. This test exists so that anyone who "fixes" it to a 404
/// has to do so deliberately.
/// </remarks>
public sealed class UnknownEventTests
{
    [Fact]
    public void UnknownEventReturnsTwoHundredWithTheAblMessage()
    {
        var result = Bella.Wms.Integration.Partners.Application
            .InboundEventRegistry.UnknownEventResult("SOMETHINGELSE");

        result.StatusCode.Should().Be(200, "the ABL returns 200 for an unroutable action");
        result.Body.Should().Be("Invalid Endpoint Action SOMETHINGELSE");
        result.HandlerSucceeded.Should().BeFalse();
    }
}
