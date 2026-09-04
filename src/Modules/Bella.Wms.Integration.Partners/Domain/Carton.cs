namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// A <c>cartonmst</c> row.
/// </summary>
/// <remarks>
/// <b>SCHEMA-INFERRED.</b> The 21 fields below were harvested from every
/// <c>cartonmst.*</c> reference in <c>api/wms</c>. Types are inferred from usage; the two
/// width constraints are stated in <c>accutechAutoBagger.i</c> rather than measured.
/// Verify against the <c>irms</c> <c>.df</c> dump.
/// </remarks>
public sealed record Carton
{
    /// <summary>SCHEMA-INFERRED: <c>cartonmst.co_num</c>.</summary>
    public required string Company { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.wh_num</c>.</summary>
    public required string Warehouse { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.carton_id</c>. The Locus <c>JobId</c>.</summary>
    public required string CartonId { get; init; }

    /// <summary>
    /// VERIFIED: <c>cartonmst.carton_num</c> — <c>integer</c>, MANDATORY,
    /// <c>INITIAL 0</c>, and the table's <b>UNIQUE PRIMARY</b> index. Was modelled as a
    /// string. It is also the key <c>cartondtl</c> joins on.
    /// </summary>
    public int? CartonNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.order</c>.</summary>
    public string? Order { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.order_suffix</c>.</summary>
    public string? OrderSuffix { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.bin_num</c>.</summary>
    public string? BinNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.box_id</c>.</summary>
    public string? BoxId { get; init; }

    /// <summary>VERIFIED: <c>cartonmst.batch</c> — <c>integer</c>, not character.</summary>
    public int? Batch { get; init; }

    /// <summary>
    /// VERIFIED: <c>cartonmst.carrier_id</c> — <c>character x(6)</c>. Joins to the
    /// <c>carrier</c> table, which is where <c>pm_irms</c> lives (the flag
    /// <c>w_pick.t</c> gates its carton state machine on).
    /// </summary>
    /// <remarks>
    /// A sibling <c>Carrier</c> property mapped to a <c>cartonmst.carrier</c> column used
    /// to sit above this one. <b>No such column exists</b> — <c>carrier</c> is a separate
    /// table. The <c>SELECT</c> naming it would have failed on the first read. Removed.
    /// </remarks>
    public string? CarrierId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.tracking_id</c>.</summary>
    public string? TrackingId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.weight</c>.</summary>
    public decimal? Weight { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.length</c>.</summary>
    public decimal? Length { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.width</c>.</summary>
    public decimal? Width { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.height</c>.</summary>
    public decimal? Height { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.row_status</c>.</summary>
    public string? RowStatus { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>cartonmst.external_status</c>. Declared <c>x(8)</c> in
    /// <c>accutechAutoBagger.i</c>. Holds AutoBagger state — <c>GetZPL</c>, <c>SendZPL</c>,
    /// <c>Hospital</c>, <c>OrdCMrcv</c>, <c>OutWithZPL</c>, <c>OutFAILED</c>, <c>OrdHDsnt</c>.
    /// Two of those constants exceed the stated width: <c>"OutWithZPL"</c> is 10
    /// characters and <c>"OutFAILED"</c> is 9. In ABL <c>x(8)</c> is a display format
    /// rather than a storage limit, so they may well be stored intact — but if the
    /// column has a genuine maximum, these values are being truncated and two distinct
    /// states collapse into one. Flagged in <c>docs/SCHEMA_REVIEW.md</c> as open item 7;
    /// the <c>.df</c> settles it.
    /// </summary>
    public string? ExternalStatus { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>cartonmst.external_id</c>. Declared <c>x(10)</c>.
    /// Values <c>AWS2SALT</c>, <c>AWS2WMS</c>.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>cartonmst.external_message</c>.</summary>
    public string? ExternalMessage { get; init; }
}
