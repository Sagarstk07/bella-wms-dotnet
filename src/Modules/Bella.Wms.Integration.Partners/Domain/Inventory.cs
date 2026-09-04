namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// A row of <c>inventory</c> — a quantity of one item, in one bin, on one pallet.
/// </summary>
/// <remarks>
/// <para>
/// <b>VERIFIED against <c>schema/irms.df</c>, 2026-09-04.</b> The table has 38 columns.
/// Every one of them is modelled here, because <c>replenputcomplete</c>
/// (<c>locusAPI.cls:3390-3428</c>) creates a row by copying 30 fields off another row —
/// a partial model would silently drop the ones it does not carry.
/// </para>
/// <para>
/// <b>The write trigger does almost nothing.</b> <c>w_invent.t</c> is 323 lines and 246 of
/// them have been compiled out since August 1996. Its entire live behaviour is
/// <c>newinv.bin_num = UPPER(newinv.bin_num)</c>. That one line is real and must be
/// reproduced on every insert and update — see <see cref="BinNumber"/>. The delete trigger
/// <c>d_invent.t</c> is a complete no-op. <c>docs/TRIGGER_RULES.md</c> has the detail.
/// </para>
/// <para>
/// <b>Four columns carry ABL defaults nothing in this handler sets.</b>
/// <c>cross_docking</c> (no), <c>cycle_cnum</c> (0), <c>qa_release_id</c> (0) and
/// <c>reserved_qty</c> (0) all have an <c>INITIAL</c> that <c>CREATE</c> applies and
/// <c>INSERT</c> does not. <c>InventoryRepository</c> writes them explicitly.
/// </para>
/// </remarks>
public sealed record Inventory
{
    /// <summary>VERIFIED: <c>inventory.co_num</c> — <c>character x(4)</c>. Not mandatory, despite leading the primary index.</summary>
    public required string Company { get; init; }

    /// <summary>VERIFIED: <c>inventory.wh_num</c> — <c>character x(4)</c>, MANDATORY.</summary>
    public required string Warehouse { get; init; }

    /// <summary>
    /// VERIFIED: <c>inventory.id</c> — <c>integer</c>, MANDATORY, UNIQUE index, allocated
    /// from the <c>inventory_id</c> sequence by <c>n_invent.t</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>inventory_id</c> is <c>CYCLE-ON-LIMIT yes</c>, like 50 of the other 54 sequences
    /// in this database. It wraps and re-issues values that live rows still hold, which is
    /// why <c>n_invent.t</c> loops. Allocate through <c>ISequenceAllocator</c>, never with a
    /// bare <c>NEXTVAL</c>.
    /// </remarks>
    public int Id { get; init; }

    /// <summary>
    /// VERIFIED: <c>inventory.bin_num</c> — <c>character x(10)</c>, MANDATORY.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Always stored uppercase.</b> That is the single surviving line of
    /// <c>w_invent.t</c>. A database trigger applies it to every writer; a repository only
    /// applies it to callers, so <c>InventoryRepository</c> upper-cases on the way in. Read
    /// paths should compare case-insensitively anyway — see the ground rules.
    /// </remarks>
    public string? BinNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.pallet_id</c> — <c>character x(17)</c>. Empty string on primary pick (shelf) locations, which is deliberate: shelf stock is split off a pallet and no longer belongs to one. (<c>locusAPI.cls:3384</c>)</summary>
    public string? PalletId { get; init; }

    /// <summary>VERIFIED: <c>inventory.abs_num</c> — <c>character x(24)</c>, MANDATORY. The item number.</summary>
    public string? ItemNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.total_qty</c> — <c>decimal</c>, 2 dp, <c>INITIAL 0</c>. Signed: the format is <c>-&gt;&gt;&gt;,&gt;&gt;9.99</c> and the ABL explicitly tests for negatives.</summary>
    public decimal TotalQuantity { get; init; }

    /// <summary>VERIFIED: <c>inventory.reserved_qty</c> — <c>decimal</c>, 2 dp, <c>INITIAL 0</c>.</summary>
    public decimal ReservedQuantity { get; init; }

    /// <summary>VERIFIED: <c>inventory.stock_stat</c> — <c>character X</c>, MANDATORY, no default. One character; compare case-insensitively.</summary>
    public string? StockStatus { get; init; }

    /// <summary>VERIFIED: <c>inventory.vendor_id</c> — <c>character X(9)</c>, MANDATORY.</summary>
    public string? VendorId { get; init; }

    /// <summary>VERIFIED: <c>inventory.lot</c> — <c>character x(24)</c>.</summary>
    public string? Lot { get; init; }

    /// <summary>VERIFIED: <c>inventory.uom</c> — <c>character X(4)</c>.</summary>
    public string? UnitOfMeasure { get; init; }

    /// <summary>VERIFIED: <c>inventory.case_qty</c> — <c>integer</c>, <c>INITIAL 1</c>. Note the default is 1, not 0.</summary>
    public int CaseQuantity { get; init; } = 1;

    /// <summary>VERIFIED: <c>inventory.expiration</c> — <c>date</c>.</summary>
    public DateOnly? Expiration { get; init; }

    /// <summary>
    /// VERIFIED: <c>inventory.date_time</c> — <c>character</c>, the 12-character
    /// <c>"YYYYMMDDHHMM"</c> form, guarded by the <c>datetim.t</c> field trigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ The <c>.df</c> display format is <c>9999-99-99 99:99</c>, which is 16 characters
    /// and does not describe the stored value. Trust the trigger, not the format.
    /// </para>
    /// <para>
    /// <b>This column orders FIFO.</b> It is the fourth field of the primary index
    /// <c>co_wh_abs_fifo</c>, so it decides which stock is consumed first. See the defect
    /// note on <c>InventoryRepository.CreateAsync</c>: the ABL never manages to set it on a
    /// replenished shelf row.
    /// </para>
    /// </remarks>
    public string? DateTime { get; init; }

    /// <summary>VERIFIED: <c>inventory.emp_num</c> — <c>character x(6)</c>.</summary>
    public string? EmployeeNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.ns_comment</c> — <c>character X(30)</c>. "Non-stock comment".</summary>
    public string? NsComment { get; init; }

    /// <summary>VERIFIED: <c>inventory.attributes</c> — <c>character x(8)</c>.</summary>
    public string? Attributes { get; init; }

    /// <summary>VERIFIED: <c>inventory.suggested_bin</c> — <c>character x(10)</c>.</summary>
    public string? SuggestedBin { get; init; }

    /// <summary>VERIFIED: <c>inventory.truck_id</c> — <c>character x(20)</c>.</summary>
    public string? TruckId { get; init; }

    /// <summary>VERIFIED: <c>inventory.po_number</c> — <c>character X(10)</c>.</summary>
    public string? PoNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.po_suffix</c> — <c>character X(2)</c>.</summary>
    public string? PoSuffix { get; init; }

    /// <summary>VERIFIED: <c>inventory.order</c> — <c>character x(10)</c>. Reserved word in SQL; quote it.</summary>
    public string? Order { get; init; }

    /// <summary>VERIFIED: <c>inventory.order_suffix</c> — <c>character X(2)</c>.</summary>
    public string? OrderSuffix { get; init; }

    /// <summary>VERIFIED: <c>inventory.item_cost</c> — <c>decimal</c>, 2 dp, <c>INITIAL 0</c>.</summary>
    public decimal ItemCost { get; init; }

    /// <summary>VERIFIED: <c>inventory.country_code</c> — <c>character XX</c>.</summary>
    public string? CountryCode { get; init; }

    /// <summary>VERIFIED: <c>inventory.cargo_control</c> — <c>character x(24)</c>. Has its own index.</summary>
    public string? CargoControl { get; init; }

    /// <summary>VERIFIED: <c>inventory.task_id</c> — <c>integer</c>. Indexed by <c>co_wh_task</c>.</summary>
    public int? TaskId { get; init; }

    /// <summary>VERIFIED: <c>inventory.qa_release_id</c> — <c>integer</c>, <c>INITIAL 0</c>. Has its own index.</summary>
    public int QaReleaseId { get; init; }

    /// <summary>VERIFIED: <c>inventory.cross_docking</c> — <c>logical</c>, <c>INITIAL no</c>.</summary>
    public bool CrossDocking { get; init; }

    /// <summary>VERIFIED: <c>inventory.cycle_flag</c> — <c>logical</c>, MANDATORY, <c>INITIAL no</c>. Set by <c>rf/wsset_cyc.p</c> — see <c>ICycleCountService</c>.</summary>
    public bool CycleFlag { get; init; }

    /// <summary>VERIFIED: <c>inventory.cycle_id</c> — <c>integer</c>.</summary>
    public int? CycleId { get; init; }

    /// <summary>VERIFIED: <c>inventory.cycle_cnum</c> — <c>integer</c>, <c>INITIAL 0</c>. "Current count number".</summary>
    public int CycleCountNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.cycle_level</c> — <c>character X(8)</c>.</summary>
    public string? CycleLevel { get; init; }

    /// <summary>VERIFIED: <c>inventory.cycle_emp_num</c> — <c>character x(6)</c>.</summary>
    public string? CycleEmployeeNumber { get; init; }

    /// <summary>VERIFIED: <c>inventory.rtn_category</c> — <c>character X(4)</c>.</summary>
    public string? ReturnCategory { get; init; }

    /// <summary>VERIFIED: <c>inventory.rtn_pallet_full</c> — <c>logical</c>, <c>INITIAL no</c>.</summary>
    public bool ReturnPalletFull { get; init; }

    /// <summary>
    /// VERIFIED: <c>inventory.custom_data</c> — <c>character X(50) EXTENT 5</c>. All five
    /// slots, because <c>replenputcomplete</c> copies all five
    /// (<c>locusAPI.cls:3414-3418</c>) without caring what any of them mean.
    /// </summary>
    /// <remarks>
    /// Over SQL-92 these are separate columns named <c>custom_data##1</c> …
    /// <c>custom_data##5</c>, which need quoting. Unlike <c>pick</c>, where slots 4 and 5
    /// have a known Locus meaning, nothing in <c>api/</c> interprets these — so they are
    /// modelled as an array rather than given business names.
    /// </remarks>
    public string?[] CustomData { get; init; } = new string?[5];
}
