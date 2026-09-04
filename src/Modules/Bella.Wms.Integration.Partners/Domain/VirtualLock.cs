namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>
/// A row of <c>virtual_lock</c> — an advisory, application-level lock on one record of one
/// table, held across user sessions.
/// </summary>
/// <remarks>
/// <para>
/// <b>VERIFIED against <c>schema/irms.df</c>, 2026-09-04.</b> All 8 columns. No triggers.
/// </para>
/// <para>
/// <b>What it is for.</b> Progress's own record locks last only as long as a transaction.
/// This table is how the RF guns hold a record across a multi-screen conversation — an
/// operator opens an inventory adjustment, walks to the shelf, and comes back. There are
/// 212 references to it across the ABL. The generic creator is <c>LockRecord()</c> in
/// <c>globals/progressproc.i:237-260</c>; <c>movemst</c> rows are locked by
/// <c>wms_webhost/wsprd_sgde.i:343</c>, the production staging guide.
/// </para>
/// <para>
/// <b><c>api/</c> only ever releases these, never takes them.</b> The one use in this module
/// is <c>replenputcomplete</c> (<c>locusAPI.cls:3322-3337</c>): before deleting a movemst
/// row it checks whether anyone is holding it and, if not, removes the lock row. So the
/// repository is deliberately read-and-delete only. Do not add a create method here without
/// working out the lock-ordering consequences first.
/// </para>
/// <para>
/// <b>⚠ Two locks are in play and they are not the same thing.</b> The row in this table is
/// the advisory lock. The ABL <i>also</i> tests <c>LOCKED(virtual_lock)</c>, which asks
/// whether another Progress session currently holds a physical record lock on this
/// <c>virtual_lock</c> row itself. Both must be honoured, and SQL-92 has no equivalent of
/// the second — see <c>IVirtualLockRepository</c>.
/// </para>
/// </remarks>
public sealed record VirtualLock
{
    /// <summary>
    /// VERIFIED: <c>virtual_lock.tablename</c> — <c>character x(8)</c>. First field of the
    /// UNIQUE primary index <c>row_key</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The declared width is 8 characters and callers pass real table names —
    /// <c>"inventory"</c> is 9 and <c>"cartonmst"</c> is 9. ABL character fields ignore the
    /// format width, so this has never mattered to the ABL. It may matter a great deal over
    /// SQL-92, which enforces the column's SQL width. See <see cref="RecordId"/>.
    /// </remarks>
    public required string TableName { get; init; }

    /// <summary>
    /// VERIFIED: <c>virtual_lock.record_id</c> — <c>character x(8)</c>. Second field of the
    /// UNIQUE primary index. Holds <c>STRING(ROWID(buffer))</c> of the locked record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>This is the sharpest edge in the table.</b> A Progress ROWID string is longer
    /// than 8 characters, and the column's declared width is 8. In the ABL that is harmless:
    /// a character field's format is a display mask, not a storage limit. Over SQL-92 it is
    /// not harmless — OpenEdge gives each character column a <i>SQL width</i> derived from
    /// that format, and a client reading a value wider than it gets an error rather than a
    /// truncated string.
    /// </para>
    /// <para>
    /// If that is what happens here, no amount of correct C# will read this table, and the
    /// fix is a DBA one (<c>dbtool</c> to widen the SQL widths), not a code one. This is the
    /// first thing to try on a real connection, and it is worth running the same check
    /// across every table this conversion touches — the answer is a property of the
    /// database, not of this column. <c>docs/SCHEMA_REVIEW.md</c> item 14.
    /// </para>
    /// </remarks>
    public required string RecordId { get; init; }

    /// <summary>VERIFIED: <c>virtual_lock.lock_user</c> — <c>character x(8)</c>. The employee number holding the lock. Indexed by <c>user_key</c>.</summary>
    public string? LockUser { get; init; }

    /// <summary>
    /// VERIFIED: <c>virtual_lock.locked_by</c> — <c>character x(8)</c>. Set to
    /// <c>THIS-PROCEDURE:FILE-NAME</c>, so it names the ABL program that took the lock.
    /// </summary>
    /// <remarks>
    /// Same width question as <see cref="RecordId"/>, and here the overflow is certain: an
    /// ABL file name such as <c>wms_webhost/wsprd_sgde.i</c> is 25 characters into an
    /// <c>x(8)</c> field. Useful for the "who is holding this" message an operator sees.
    /// </remarks>
    public string? LockedBy { get; init; }

    /// <summary>
    /// VERIFIED: <c>virtual_lock.lock_date</c> — <c>date</c>, <c>INITIAL TODAY</c>. Indexed
    /// descending by <c>date_key</c>, which is how stale locks get found.
    /// </summary>
    /// <remarks>
    /// A date, not a timestamp — a lock cannot be aged out in less than a day from this
    /// column alone. And <c>INITIAL TODAY</c> is an ABL default: an <c>INSERT</c> that omits
    /// it stores the unknown value, not today's date.
    /// </remarks>
    public DateOnly? LockDate { get; init; }

    /// <summary>VERIFIED: <c>virtual_lock.custom1</c> — <c>character x(8)</c>. Unused by <c>api/</c>.</summary>
    public string? Custom1 { get; init; }

    /// <summary>VERIFIED: <c>virtual_lock.custom2</c> — <c>character x(8)</c>. Unused by <c>api/</c>.</summary>
    public string? Custom2 { get; init; }

    /// <summary>VERIFIED: <c>virtual_lock.custom3</c> — <c>character x(8)</c>. Unused by <c>api/</c>.</summary>
    public string? Custom3 { get; init; }
}
