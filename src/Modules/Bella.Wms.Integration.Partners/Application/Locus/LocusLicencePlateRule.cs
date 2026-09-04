namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>
/// The first-character rule shared by two places in the ABL: the router's licence-plate
/// prefix (<c>wsLocusAPI.p:259-265</c>, in <see cref="LocusEventRouter"/>) and
/// <c>PUTAWAYREQUEST</c>'s branch decision (<c>locusAPI.cls:3210-3241</c>, in
/// <see cref="LocusPutawayRequestHandler"/>) — <c>docs/HANDLER_SPECS.md</c> Group 5 is
/// explicit that <c>putawayrequest</c> "branches on the same first-character rule as the
/// router".
/// </summary>
/// <remarks>
/// A value whose first character parses as an integer is a replenishment/SSCC-18;
/// anything else — including an empty value, since <c>INT("")</c> raises an error in the
/// ABL — is a putaway/tote.
/// </remarks>
internal static class LocusLicencePlateRule
{
    public static bool FirstCharacterIsNumeric(string value)
    {
        var firstCharacter = value.Length > 0 ? value[0..1] : string.Empty;
        return int.TryParse(firstCharacter, out _);
    }
}
