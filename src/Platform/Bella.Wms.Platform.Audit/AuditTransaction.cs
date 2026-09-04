namespace Bella.Wms.Platform.Audit;

/// <summary>
/// One row of the <c>transactions</c> audit table.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCHEMA-INFERRED.</b> Every field below was harvested from ABL source, not from a
/// <c>.df</c> dump. Types are inferred from usage. Verify all of it against the
/// <c>irms</c> schema before first run — see <c>docs/SCHEMA_REVIEW.md</c>.
/// </para>
/// <para>
/// The reference write site is <c>locusAPI.cls:3040-3060</c>, which creates a <c>JE</c>
/// transaction on picking failure and assigns 18 fields. The <c>IG</c> read at
/// <c>locusAPI.cls:3013</c> supplies the rest.
/// </para>
/// <para>
/// <b>Why this is a Wave 1 deliverable.</b> Phase 6 §5: the <c>n_trans.t</c> CREATE
/// trigger on this table becomes application logic, and 23 boundaries write
/// <c>transactions</c> directly today. A trigger fires no matter who writes; a service
/// only fires if the writer calls it. So every writer must go through this service or the
/// audit trail silently loses rows — which is why the audit service ships before any of
/// its writers move.
/// </para>
/// </remarks>
public sealed record AuditTransaction
{
    /// <summary>SCHEMA-INFERRED: <c>transactions.co_num</c>.</summary>
    public required string Company { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.wh_num</c>.</summary>
    public required string Warehouse { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>transactions.trans_type</c>. Two-character code.
    /// Values seen in <c>api/wms</c>: <c>IG</c> (pick), <c>JE</c> (pick error),
    /// <c>JZ</c> (Locus pick/putaway audit, per the module AGENTS.md).
    /// </summary>
    public required string TransactionType { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.item_type</c> — <c>character X</c>, <b>MANDATORY</b>,
    /// <c>INITIAL "S"</c>. The schema's own description gives the domain:
    /// <c>S</c> stock, <c>N</c> non-stock, <c>L</c> labour, <c>C</c> consumable.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"S"</c> because ABL <c>CREATE</c> applies the schema INITIAL and a
    /// SQL INSERT does not. Omitting it violates the MANDATORY constraint — this column
    /// was missing from the insert entirely until the schema arrived, so no audit row
    /// could ever have been written.
    /// </remarks>
    public string ItemType { get; init; } = "S";

    /// <summary>SCHEMA-INFERRED: <c>transactions.emp_num</c>. Stamped with the API user (BEL-662).</summary>
    public required string EmployeeNumber { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.trans_num</c> — <c>integer</c>, MANDATORY, <c>INITIAL ?</c>,
    /// with a UNIQUE index. <b>32-bit, not 64.</b> It was modelled as <c>long</c>; the real
    /// column cannot hold one. Because the initial value is unknown and the field is
    /// mandatory, a SQL INSERT that omits it violates the constraint — the sequence work in
    /// <c>docs/SCHEMA_REVIEW.md</c> item 9 is required, not optional.
    /// </summary>
    public int? TransactionNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.carton_id</c>.</summary>
    public string? CartonId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.po_number</c> — the order number.</summary>
    public string? OrderNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.po_suffix</c>.</summary>
    public string? OrderSuffix { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.abs_num</c> — the item number.</summary>
    public string? ItemNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.bin_num</c>.</summary>
    public string? BinNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.bin_from</c>.</summary>
    public string? BinFrom { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.bin_to</c>.</summary>
    public string? BinTo { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.lot</c>.</summary>
    public string? Lot { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.sugg_qty</c> — the suggested/task quantity.</summary>
    public decimal? SuggestedQuantity { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.item_qty</c> — the actual quantity.</summary>
    public decimal? ItemQuantity { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.case_qty</c> — <c>integer</c>, not decimal. Note that its
    /// neighbours <c>item_qty</c> and <c>sugg_qty</c> <i>are</i> decimal(2); cases are
    /// counted whole, pieces are not.
    /// </summary>
    public int? CaseQuantity { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.link_id</c>. Holds the tote id on Locus picks.</summary>
    public string? LinkId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.doc_id</c>. Holds the pick id on Locus picks.</summary>
    public string? DocumentId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.task_id</c>.</summary>
    public string? TaskId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.pallet_id</c>.</summary>
    public string? PalletId { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.pallet_id_from</c> — <c>character x(17)</c>. The pallet the
    /// stock came off, where <see cref="PalletId"/> is the one it went onto.
    /// </summary>
    /// <remarks>
    /// Added 2026-09-04 for <c>replenputcomplete</c>, which sets it on both the <c>AS</c>
    /// and <c>MR</c> rows (<c>locusAPI.cls:3486, 3540</c>). A replenishment onto a primary
    /// pick shelf leaves <see cref="PalletId"/> empty and this populated, which is how the
    /// audit trail records that stock was split off a pallet onto a shelf.
    /// </remarks>
    public string? PalletIdFrom { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.mach_type</c> — <c>character X</c>, one character. The kind
    /// of device that caused the movement, from <c>static_connect:user_type</c>.
    /// </summary>
    /// <remarks>
    /// Added 2026-09-04. Only the <c>AS</c> row in <c>replenputcomplete</c> sets it
    /// (<c>locusAPI.cls:3498</c>); the <c>MR</c> row's assignment is commented out at
    /// line 3547. That asymmetry is in the ABL and is reproduced, not tidied.
    /// </remarks>
    public string? MachType { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.lpn_scanid</c>.</summary>
    public string? LpnScanId { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.comments</c> — <c>character</c>, display format
    /// <c>x(50)</c> but MAX-WIDTH 2854, so long text is stored, not truncated.
    /// </summary>
    /// <remarks>
    /// A sibling <c>Comment</c> (singular) property used to sit here, mapped to a
    /// <c>transactions.comment</c> column. <b>That column does not exist.</b> The
    /// confusion came from <c>locusAPI.cls:3021</c> assigning <c>xtrans.comment</c> —
    /// <c>xtrans</c> is a buffer on a different table. <c>comment</c> is in fact a
    /// separate table in <c>irms</c>. Removed; do not reintroduce it.
    /// </remarks>
    public string? Comments { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.ns_comment</c>. Carries the queue delay on Locus picks.</summary>
    public string? NsComment { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.row_status</c> — <c>character X</c> (one character),
    /// <c>INITIAL "O"</c>. The lifecycle is <c>O</c> open → <c>C</c> complete / <c>E</c>
    /// error, which is what the reject family's idempotency guard tests for.
    /// </summary>
    /// <remarks>
    /// <b>Defaults to "O" here on purpose.</b> ABL <c>CREATE</c> applies the schema's
    /// INITIAL value; a SQL INSERT does not. Leaving this null would write blank rows that
    /// the ABL side would then read as neither open nor closed.
    /// <br/>
    /// Note that <c>row_status</c> is <b>not</b> uniformly typed across this schema:
    /// on <c>empmst</c> the same column name is a <c>logical</c>, not a character.
    /// </remarks>
    public string? RowStatus { get; init; } = "O";

    /// <summary>
    /// VERIFIED: <c>transactions.void</c> — <c>logical</c>, <c>INITIAL no</c>. Renamed
    /// because <c>void</c> is a C# keyword. Defaults to <see langword="false"/> for the
    /// same reason <see cref="RowStatus"/> defaults: a SQL INSERT applies no schema default.
    /// </summary>
    public bool? IsVoid { get; init; } = false;

    /// <summary>SCHEMA-INFERRED: <c>transactions.dept_num</c>. From ambient context.</summary>
    public int? DepartmentNumber { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.shf_num</c>. From ambient context.</summary>
    public int? ShiftNumber { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>transactions.date_time</c>. <b>Not a datetime.</b> A
    /// 12-character <c>"YYYYMMDDHHMM"</c> string, minute precision only, built by the
    /// <c>n_trans.t</c> CREATE trigger as
    /// <c>SUBSTRING(sdate,7,4) + SUBSTRING(sdate,1,2) + SUBSTRING(sdate,4,2) +
    /// SUBSTRING(stime,1,2) + SUBSTRING(stime,4,2)</c> — see
    /// <c>docs/TRIGGER_RULES.md</c>. Previously modelled as <c>DateTimeOffset</c>, which
    /// was wrong.
    /// </summary>
    /// <remarks>
    /// <b>VERIFIED, and the database enforces it.</b> The <c>.df</c> declares a
    /// field-level trigger on this column — <c>FIELD-TRIGGER "Assign" OVERRIDE PROCEDURE
    /// "datetim.t"</c> — which validates the string positionally: year 1-4, month 5-6,
    /// day 7-8, hour 9-10 (must be 00–23), minute 11-12 (must be 00–59), and does
    /// <c>RETURN ERROR</c> otherwise. Empty and unknown are allowed; anything else
    /// malformed is <b>rejected by the database</b>, not silently stored.
    /// <br/>
    /// So the <c>"yyyyMMddHHmm"</c> format used here is not a guess that happens to match
    /// the trigger — it is the only form the column accepts. The same trigger guards
    /// <c>date_time</c> on <c>inventory</c>, <c>pick</c>, <c>palletdet</c> and
    /// <c>shpmst</c>; there are 12 such field triggers in the schema and they appear in
    /// no <c>.p</c> file.
    /// </remarks>
    public string? DateTime { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>transactions.trans_sec_time</c>. ABL <c>TIME</c> — raw seconds
    /// since midnight (0-86399) — captured by <c>n_trans.t</c> alongside
    /// <see cref="DateTime"/> to arbitrate ordering between rows landing in the same
    /// minute, since <see cref="DateTime"/> has only minute precision.
    /// </summary>
    public int? TransSecTime { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>transactions.proc_created</c>. ABL <c>program-name(2)</c> —
    /// the calling program, two frames up the ABL call stack at CREATE time. No
    /// established .NET mapping yet; the caller supplies it if known. Distinct from
    /// <see cref="OriginStack"/>, which is a new column with no ABL equivalent at all.
    /// </summary>
    public string? ProcCreated { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.batch</c>.</summary>
    public string? Batch { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.stock_stat</c>.</summary>
    public string? StockStatus { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.old_stock_stat</c>.</summary>
    public string? OldStockStatus { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>transactions.adj_code</c>.</summary>
    public string? AdjustmentCode { get; init; }

    /// <summary>
    /// VERIFIED: <c>transactions.custom_data</c> is declared <c>EXTENT 5</c> — a Progress
    /// array of five <c>X(50)</c> slots, surfacing over SQL-92 as
    /// <c>custom_data##1</c>..<c>custom_data##5</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Changed 2026-09-04 from a single string to the array it always was.</b> It was
    /// briefly not written at all — correct at the time, since nothing in <c>api/</c> set
    /// it. <c>replenputcomplete</c> does: the <c>MR</c> row carries the item's
    /// <c>custom_data[1]</c> and the replenishment build time in <c>[4]</c>
    /// (<c>locusAPI.cls:3550, 3553</c>).
    /// </para>
    /// <para>
    /// Every table checked carries this same five-slot array. Slots mean different things
    /// on different tables and nothing enforces that — on <c>pick</c>, 4 and 5 are the Locus
    /// robot and tote; on <c>movemst</c>, 1 is the licence plate. Read the ABL before
    /// assuming a slot means the same thing here as it does there.
    /// </para>
    /// </remarks>
    public string?[] CustomData { get; init; } = new string?[5];

    /// <summary><c>custom_data[1]</c>. Bound individually because SQL-92 sees five columns, not an array.</summary>
    public string? CustomData1 => Slot(0);

    /// <summary><c>custom_data[2]</c>.</summary>
    public string? CustomData2 => Slot(1);

    /// <summary><c>custom_data[3]</c>.</summary>
    public string? CustomData3 => Slot(2);

    /// <summary><c>custom_data[4]</c>.</summary>
    public string? CustomData4 => Slot(3);

    /// <summary><c>custom_data[5]</c>.</summary>
    public string? CustomData5 => Slot(4);

    private string? Slot(int index) =>
        CustomData is not null && index < CustomData.Length ? CustomData[index] : null;

    /// <summary>
    /// The origin marker Phase 6 §9 requires: "Add a source marker so a production
    /// incident can be traced to the stack that caused it." No ABL equivalent — this
    /// column does not exist yet and must be added, or an existing free-text column
    /// agreed for the purpose. Raised in <c>docs/SCHEMA_REVIEW.md</c> as an open item.
    /// </summary>
    public string? OriginStack { get; init; }

    /// <summary>
    /// Fills in the fields a caller does not have to supply: the ambient
    /// <c>static_connect</c> values, and the two <c>n_trans.t</c> timestamp columns.
    /// </summary>
    /// <param name="departmentNumber">Ambient <c>static_connect</c> department (<c>wsMiddlewareAPI.p:311-315</c>).</param>
    /// <param name="shiftNumber">Ambient <c>static_connect</c> shift.</param>
    /// <param name="originStack">The Phase 6 §9 source marker.</param>
    /// <param name="now">
    /// A single captured instant. Both timestamp fields are derived from it, because the
    /// ABL derives its <c>date_time</c> and <c>trans_sec_time</c> from one
    /// <c>sdate</c>/<c>stime</c>/<c>TIME</c> capture at CREATE time. Two independent
    /// "now" reads could straddle a second boundary and produce a row whose minute and
    /// second-of-day disagree.
    /// </param>
    /// <remarks>
    /// This lives on the record rather than in <c>AuditService</c> so the in-memory fake
    /// enriches identically instead of reimplementing it. It was reimplemented once, and
    /// the copy drifted: when <c>DateTime</c> changed from <c>DateTimeOffset?</c> to the
    /// <c>n_trans.t</c> 12-character string, the fake kept assigning
    /// <c>DateTimeOffset.Now</c> and nobody found out, because nothing referenced the
    /// Fakes project and so nothing compiled it.
    /// </remarks>
    public AuditTransaction Enrich(
        int? departmentNumber,
        int? shiftNumber,
        string? originStack,
        DateTimeOffset now) =>
        this with
        {
            DepartmentNumber = DepartmentNumber ?? departmentNumber,
            ShiftNumber = ShiftNumber ?? shiftNumber,

            // n_trans.t: SUBSTRING(sdate,7,4) + SUBSTRING(sdate,1,2) + SUBSTRING(sdate,4,2)
            // + SUBSTRING(stime,1,2) + SUBSTRING(stime,4,2) — "YYYYMMDDHHMM", minute
            // precision. One format string reproduces the same five-substring assembly.
            DateTime = DateTime ?? now.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),

            // ABL TIME: raw seconds since midnight, local time.
            TransSecTime = TransSecTime ?? (int)now.TimeOfDay.TotalSeconds,

            OriginStack = OriginStack ?? originStack,
        };
}
