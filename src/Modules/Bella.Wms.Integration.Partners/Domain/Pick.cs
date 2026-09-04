namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// A row of <c>pick</c> — one line of work for one carton.
/// </summary>
/// <remarks>
/// <para>
/// <b>VERIFIED against <c>schema/irms.df</c>, 2026-09-03.</b> The table has 37 columns;
/// modelled here are the ones <c>api/</c> reads or writes, plus the identifying key. The
/// rest are in the dump and in <c>docs/SCHEMA_REVIEW.md</c> — add them when something
/// needs them, rather than carrying 37 properties nobody reads.
/// </para>
/// <para>
/// <b>The tote assignment lives in an array.</b> Locus does not get its own columns on this
/// table. <c>locusAPI.cls:2309-2311</c> writes the robot and tote ids into slots 4 and 5 of
/// <c>custom_data</c>, a five-element <c>X(50)</c> extent field that every table in this
/// schema carries. See <see cref="ToteRobot"/>.
/// </para>
/// </remarks>
public sealed record Pick
{
    /// <summary>VERIFIED: <c>pick.co_num</c> — <c>character x(4)</c>, MANDATORY.</summary>
    public required string Company { get; init; }

    /// <summary>VERIFIED: <c>pick.wh_num</c> — <c>character x(4)</c>, MANDATORY.</summary>
    public required string Warehouse { get; init; }

    /// <summary>
    /// VERIFIED: <c>pick.id</c> — <c>integer</c>, with a UNIQUE index. The row's own key.
    /// Note the <i>primary</i> index is <c>co_wh_batch_sequence_bin</c>, not this.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// VERIFIED: <c>pick.carton_id</c> — <c>character x(17)</c>. Indexed by
    /// <c>co_wh_carton</c> (co_num, wh_num, carton_id), which is the lookup the tote
    /// handlers use.
    /// </summary>
    /// <remarks>
    /// One character shorter than <c>cartonmst.carton_id</c>, which is <c>x(18)</c>. Worth
    /// knowing before anything assumes the two are interchangeable.
    /// </remarks>
    public string? CartonId { get; init; }

    /// <summary>
    /// VERIFIED: <c>pick.pick_status</c> — <c>character X</c>, <c>INITIAL "O"</c>. The
    /// schema's description says "same statuses as order detail".
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Compare this case-insensitively.</b> The stored default is uppercase <c>"O"</c>
    /// and the ABL queries it as lowercase <c>"o"</c> — at <c>locusAPI.cls:2300</c> and in
    /// five other places across four modules. ABL does not care; SQL does. See the ground
    /// rules in <c>CLAUDE.md</c>.
    /// </remarks>
    public string? PickStatus { get; init; }

    /// <summary>VERIFIED: <c>pick.allow_pick</c> — <c>logical</c>, <c>INITIAL yes</c>. Appears in five indexes alongside <c>pick_status</c>.</summary>
    public bool? AllowPick { get; init; }

    /// <summary>VERIFIED: <c>pick.order</c> — <c>character x(10)</c>. Two shorter than <c>cartonmst.order</c>, which is <c>x(12)</c>.</summary>
    public string? Order { get; init; }

    /// <summary>VERIFIED: <c>pick.order_suffix</c> — <c>character X(2)</c>.</summary>
    public string? OrderSuffix { get; init; }

    /// <summary>VERIFIED: <c>pick.line</c> — <c>integer</c>, MANDATORY, <c>INITIAL 1</c>.</summary>
    public int? Line { get; init; }

    /// <summary>VERIFIED: <c>pick.line_sequence</c> — <c>integer</c>, <c>INITIAL 0</c>.</summary>
    public int? LineSequence { get; init; }

    /// <summary>VERIFIED: <c>pick.abs_num</c> — <c>character x(24)</c>. The item number.</summary>
    public string? ItemNumber { get; init; }

    /// <summary>VERIFIED: <c>pick.bin_num</c> — <c>character x(10)</c>, MANDATORY.</summary>
    public string? BinNumber { get; init; }

    /// <summary>VERIFIED: <c>pick.qty</c> — <c>decimal</c>, 2 dp, <c>INITIAL 0</c>.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>VERIFIED: <c>pick.orig_qty</c> — <c>decimal</c>, 2 dp, <c>INITIAL 0</c>.</summary>
    public decimal? OriginalQuantity { get; init; }

    /// <summary>VERIFIED: <c>pick.lot</c> — <c>character x(24)</c>.</summary>
    public string? Lot { get; init; }

    /// <summary>VERIFIED: <c>pick.batch</c> — <c>integer</c>, <c>INITIAL 0</c>. Part of the primary index.</summary>
    public int? Batch { get; init; }

    /// <summary>VERIFIED: <c>pick.pick_sequence</c> — <c>integer</c>, format <c>999</c>. Part of the primary index.</summary>
    public int? PickSequence { get; init; }

    /// <summary>
    /// VERIFIED: <c>pick.date_time</c> — <c>character</c>, the same 12-character
    /// <c>"YYYYMMDDHHMM"</c> form as <c>transactions.date_time</c>, and guarded by the same
    /// <c>datetim.t</c> field trigger. Malformed values are rejected by the database.
    /// </summary>
    public string? DateTime { get; init; }

    /// <summary>VERIFIED: <c>pick.external_tote</c> — <c>character x(18)</c>.</summary>
    /// <remarks>
    /// ⚠ Present, and <b>not</b> what the tote handlers write. They use
    /// <c>custom_data[5]</c> instead (<c>locusAPI.cls:2311</c>). Whether this column is
    /// dead, used by another module, or an abandoned first attempt is a question for the
    /// warehouse team — do not write to it on the assumption that it is the "proper" home.
    /// </remarks>
    public string? ExternalTote { get; init; }

    /// <summary>VERIFIED: <c>pick.external_status</c> — <c>character x(8)</c>. Same width as the <c>cartonmst</c> column of the same name.</summary>
    public string? ExternalStatus { get; init; }

    /// <summary>VERIFIED: <c>pick.external_message</c> — <c>character x(78)</c>.</summary>
    public string? ExternalMessage { get; init; }

    /// <summary>
    /// The Locus robot that owns this pick — <c>pick.custom_data[4]</c>, written at
    /// <c>locusAPI.cls:2310</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is an array slot, not a column.</b> <c>custom_data</c> is declared
    /// <c>EXTENT 5</c> of <c>X(50)</c>, so over SQL-92 the element is addressed as
    /// <c>"custom_data##4"</c>. The name here is the meaning `api/` gives slot 4; other
    /// modules may use the same slot for something else, which is the hazard of storing
    /// business data in a generic array.
    /// </remarks>
    public string? ToteRobot { get; init; }

    /// <summary>
    /// The Locus tote this pick is inducted into — <c>pick.custom_data[5]</c>, written at
    /// <c>locusAPI.cls:2311</c>. See <see cref="ToteRobot"/> for how the array maps.
    /// </summary>
    public string? ToteId { get; init; }
}
