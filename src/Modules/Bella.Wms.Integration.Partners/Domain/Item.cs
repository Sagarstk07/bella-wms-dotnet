namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// The two fields of <c>item</c> that the putaway path reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately tiny.</b> <c>item</c> is one of the largest tables in the schema and it
/// belongs to the item-master module, not to this one. Modelling it fully here would make
/// Partners look like the owner of item data. Two fields, read-only, and the type says so.
/// </para>
/// <para>
/// VERIFIED against <c>schema/irms.df</c>, 2026-09-04.
/// </para>
/// </remarks>
public sealed record Item
{
    /// <summary>VERIFIED: <c>item.abs_num</c> — <c>character x(24)</c>, MANDATORY.</summary>
    public required string ItemNumber { get; init; }

    /// <summary>
    /// VERIFIED: <c>item.item_type</c> — <c>character X</c>, MANDATORY, <c>INITIAL "S"</c>.
    /// Copied onto <c>transactions.item_type</c>, which has the same constraint.
    /// </summary>
    /// <remarks>
    /// When no item row exists, <c>replenputcomplete</c> substitutes <c>"N"</c>
    /// (<c>locusAPI.cls:3480</c>) — not the schema default <c>"S"</c>. That distinction is
    /// how an audit row records "this item is not in the master".
    /// </remarks>
    public required string ItemType { get; init; }

    /// <summary>
    /// VERIFIED: <c>item.case_qty</c> — <c>integer</c>, <c>INITIAL 1</c>.
    /// </summary>
    /// <remarks>
    /// <c>static_connect:get_case_qty</c> (<c>wms_webhost/static_connect.cls:2706-2727</c>)
    /// returns <b>1</b>, not 0, when the item is not found — see
    /// <c>IItemRepository.GetCaseQuantityAsync</c>.
    /// </remarks>
    public int CaseQuantity { get; init; } = 1;
}
