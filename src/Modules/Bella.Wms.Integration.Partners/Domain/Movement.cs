namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// A row of <c>movemst</c> — one outstanding instruction to move stock from one bin to
/// another. The work order that a Locus robot executes.
/// </summary>
/// <remarks>
/// <para>
/// <b>VERIFIED against <c>schema/irms.df</c>, 2026-09-04.</b> All 26 columns are modelled.
/// </para>
/// <para>
/// <b>A movemst row is deleted when the move completes, not closed.</b>
/// <c>replenputcomplete</c> copies the row to a temp-table, deletes it, and then works from
/// the copy (<c>locusAPI.cls:3338-3346</c>). There is a <c>row_status</c> column and it is
/// MANDATORY, but this path does not use it as a lifecycle — the row simply stops existing.
/// Anything asking "what moves happened" must read <c>transactions</c>, not this table.
/// </para>
/// <para>
/// <b>Locus identifiers live in <c>custom_data</c>.</b> Slot 1 carries the SSCC-18 licence
/// plate and slot 4 carries the time the replenishment was built. Both are read by
/// <c>replenputcomplete</c> (<c>locusAPI.cls:3295, 3305</c>) and slot 1 is half the lookup
/// key. This is the same "business data in a generic array" pattern as <c>pick</c>.
/// </para>
/// <para>
/// Only one trigger fires here: <c>n_move.t</c> on CREATE, which allocates <c>id</c> from
/// the <c>movemst_id</c> sequence and nothing else.
/// </para>
/// </remarks>
public sealed record Movement
{
    /// <summary>VERIFIED: <c>movemst.co_num</c> — <c>character x(4)</c>. Leads the primary index <c>co_wh_from_abs_status</c>.</summary>
    public required string Company { get; init; }

    /// <summary>VERIFIED: <c>movemst.wh_num</c> — <c>character x(4)</c>, MANDATORY.</summary>
    public required string Warehouse { get; init; }

    /// <summary>
    /// VERIFIED: <c>movemst.id</c> — <c>integer</c>, MANDATORY, UNIQUE, allocated from the
    /// cycling <c>movemst_id</c> sequence by <c>n_move.t</c>.
    /// </summary>
    /// <remarks>
    /// This id is written into <c>transactions.link_id</c> — which is
    /// <c>character x(30)</c>, so the join is int-to-string. <c>replenputcomplete</c> finds
    /// the open <c>FM</c> transaction with <c>link_id EQ STRING(movemst.id)</c>
    /// (<c>locusAPI.cls:3312</c>).
    /// </remarks>
    public int Id { get; init; }

    /// <summary>VERIFIED: <c>movemst.abs_num</c> — <c>character x(24)</c>, MANDATORY. The item number.</summary>
    public string? ItemNumber { get; init; }

    /// <summary>
    /// VERIFIED: <c>movemst.bin_from</c> — <c>character x(10)</c>, MANDATORY.
    /// </summary>
    /// <remarks>
    /// On a Locus replenishment this holds the <i>robot</i> id, not a warehouse bin — the
    /// robot is modelled as the location the stock is currently on. That is why
    /// <c>replenputcomplete</c> looks the row up by <c>bin_from EQ cRobot</c>
    /// (<c>locusAPI.cls:3283</c>) and why the ABL annotates the field <c>/* robot */</c> at
    /// line 3350.
    /// </remarks>
    public string? BinFrom { get; init; }

    /// <summary>VERIFIED: <c>movemst.bin_to</c> — <c>character x(10)</c>, MANDATORY. The destination shelf.</summary>
    public string? BinTo { get; init; }

    /// <summary>VERIFIED: <c>movemst.zone_to</c> — <c>character x(10)</c>.</summary>
    public string? ZoneTo { get; init; }

    /// <summary>VERIFIED: <c>movemst.to_wh_num</c> — <c>character x(4)</c>, MANDATORY, <c>INITIAL "0"</c>. A character field whose default is the string "0", not a number.</summary>
    public string? ToWarehouse { get; init; }

    /// <summary>VERIFIED: <c>movemst.wh_zone</c> — <c>character XX</c>, MANDATORY, <c>INITIAL "P"</c>.</summary>
    public string? WarehouseZone { get; init; }

    /// <summary>VERIFIED: <c>movemst.movement_type</c> — <c>character x(2)</c>, MANDATORY, no default.</summary>
    public string? MovementType { get; init; }

    /// <summary>VERIFIED: <c>movemst.row_status</c> — <c>character X</c>, MANDATORY, no default. See the type remarks: this path deletes rather than closes.</summary>
    public string? RowStatus { get; init; }

    /// <summary>VERIFIED: <c>movemst.quantity</c> — <c>decimal</c>, <c>INITIAL 0</c>. Note the format carries no decimal places even though the type is decimal.</summary>
    public decimal Quantity { get; init; }

    /// <summary>VERIFIED: <c>movemst.pallet_id</c> — <c>character x(17)</c>.</summary>
    public string? PalletId { get; init; }

    /// <summary>VERIFIED: <c>movemst.lot</c> — <c>character x(24)</c>.</summary>
    public string? Lot { get; init; }

    /// <summary>VERIFIED: <c>movemst.lpn_scanid</c> — <c>character x(24)</c>.</summary>
    public string? LpnScanId { get; init; }

    /// <summary>VERIFIED: <c>movemst.truck_id</c> — <c>character x(20)</c>. Indexed by <c>co_wh_truck</c>.</summary>
    public string? TruckId { get; init; }

    /// <summary>VERIFIED: <c>movemst.doc_id</c> — <c>character X(10)</c>.</summary>
    public string? DocumentId { get; init; }

    /// <summary>VERIFIED: <c>movemst.task_id</c> — <c>integer</c>. Indexed by <c>co_wh_task</c>.</summary>
    public int? TaskId { get; init; }

    /// <summary>VERIFIED: <c>movemst.batch</c> — <c>integer</c>, <c>INITIAL 0</c>. Leads the <c>batch_item_from_to</c> index.</summary>
    public int Batch { get; init; }

    /// <summary>VERIFIED: <c>movemst.dept_num</c> — <c>integer</c>, MANDATORY, <c>INITIAL 0</c>.</summary>
    public int DepartmentNumber { get; init; }

    /// <summary>VERIFIED: <c>movemst.machine</c> — <c>character X</c>.</summary>
    public string? Machine { get; init; }

    /// <summary>VERIFIED: <c>movemst.prod_id</c> — <c>integer</c>.</summary>
    public int? ProductionId { get; init; }

    /// <summary>VERIFIED: <c>movemst.prod_line</c> — <c>integer</c>.</summary>
    public int? ProductionLine { get; init; }

    /// <summary>VERIFIED: <c>movemst.urgent</c> — <c>logical</c>, <c>INITIAL no</c>.</summary>
    public bool Urgent { get; init; }

    /// <summary>
    /// VERIFIED: <c>movemst.date_time</c> — <c>character</c>, same 12-character
    /// <c>"YYYYMMDDHHMM"</c> convention as everywhere else.
    /// </summary>
    /// <remarks>
    /// ⚠ Unlike <c>inventory.date_time</c>, <c>pick.date_time</c> and
    /// <c>transactions.date_time</c>, this column has <b>no <c>datetim.t</c> field
    /// trigger</b>. Nothing validates the format. Treat values read from it as untrusted.
    /// </remarks>
    public string? DateTime { get; init; }

    /// <summary>
    /// VERIFIED: <c>movemst.custom_data</c> — <c>character X(50) EXTENT 5</c>. Slot 1 is the
    /// SSCC-18 licence plate, slot 4 is the replenishment build time. See the type remarks.
    /// </summary>
    public string?[] CustomData { get; init; } = new string?[5];

    /// <summary>
    /// The row's physical address, as OpenEdge SQL-92 reports it. Not a column — a
    /// pseudo-column selected alongside the others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>UNVERIFIED, and it matters.</b> <c>replenputcomplete</c> releases the record's
    /// <c>virtual_lock</c> by looking it up on <c>STRING(ROWID(bmovemst))</c>
    /// (<c>locusAPI.cls:3324</c>). Whether SQL-92's <c>ROWID</c> renders to the same string
    /// ABL's <c>STRING(ROWID())</c> produces has never been checked against a real database.
    /// </para>
    /// <para>
    /// <b>If the two forms differ, the lookup silently finds nothing</b> — and "no lock row"
    /// is indistinguishable from "not locked", so .NET would proceed to delete a movemst row
    /// that a user on the RF gun currently holds. That is the failure mode to test for
    /// first. <c>docs/SCHEMA_REVIEW.md</c> item 13.
    /// </para>
    /// </remarks>
    public string? RowIdentity { get; init; }
}
