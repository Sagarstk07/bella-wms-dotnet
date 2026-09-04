using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bella.Wms.Integration.Partners.Contracts.Locus;

/// <summary>
/// The one canonical <see cref="JsonSerializerOptions"/> for the Locus wire format.
/// </summary>
/// <remarks>
/// <para>
/// Every DTO in this namespace carries an explicit <see cref="JsonPropertyNameAttribute"/>,
/// so no naming policy is configured and none should be added — the property names are
/// the contract, and a policy would let a missing attribute silently produce a different
/// name on the wire.
/// </para>
/// <para>
/// <b>Where this lives, and why not on <c>LocusEventRouter</c>.</b> The router does not
/// serialize: it reads the envelope with <c>JsonDocument.Parse</c> and hands the raw
/// payload to a handler. These options belong beside the contracts they apply to, so the
/// inbound router, the outbound <c>ILocusClient</c> and the contract tests can all share
/// one instance without the Contracts assembly having to reference Application.
/// </para>
/// <para>
/// <b>Both settings below are behavioural choices, not defaults.</b> Each mirrors what
/// the ABL does, and each should be confirmed against a week of captured traffic — see
/// <c>docs/SCHEMA_REVIEW.md</c>. They are the kind of thing that works for years and then
/// fails on one partner's payload.
/// </para>
/// </remarks>
public static class LocusJson
{
    /// <summary>
    /// Shared options for reading and writing Locus payloads. Treat as immutable —
    /// <see cref="JsonSerializerOptions"/> locks itself on first use.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        // CASE-SENSITIVE, deliberately — this is the System.Text.Json default and it is
        // also what the ABL does. wsLocusAPI.p:131-300 dispatches on
        // jsoRequest:Has("OrderJobResult"), and OpenEdge JsonObject:Has is case-sensitive.
        // Turning this on would make us accept payloads the ABL rejects, which during the
        // parallel run means the two stacks disagree about what is a valid request.
        PropertyNameCaseInsensitive = false,

        // Omit nulls when writing. A Progress JsonObject contains only the properties the
        // code explicitly added, so a field the ABL never set is ABSENT from the payload,
        // not present-and-null. Writing explicit nulls would put keys on the wire that
        // Locus has never received from this interface.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
