using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// The outcome of asking whether a record is held.
/// </summary>
/// <remarks>
/// Three states, because the ABL distinguishes three
/// (<c>locusAPI.cls:3322-3337</c>) and collapsing any two changes behaviour.
/// </remarks>
public enum VirtualLockState
{
    /// <summary>
    /// No lock row exists. The record is free. The ABL falls through both branches and
    /// carries on.
    /// </summary>
    NotLocked = 0,

    /// <summary>
    /// A lock row exists and this session can take it. The ABL deletes it and carries on —
    /// so an advisory lock left behind by a finished session is treated as stale and cleared,
    /// not as a reason to refuse.
    /// </summary>
    Releasable = 1,

    /// <summary>
    /// Another session physically holds the lock row right now — ABL <c>LOCKED(virtual_lock)</c>
    /// after a <c>NO-WAIT</c> find. The caller must abandon the whole operation.
    /// </summary>
    HeldByAnotherSession = 2,
}

/// <summary>
/// Reads and releases <c>virtual_lock</c> — the application-level record locks the RF guns
/// hold across screens.
/// </summary>
/// <remarks>
/// <para>
/// <b>No create method, on purpose.</b> <c>api/</c> only releases. See <see cref="VirtualLock"/>.
/// </para>
/// <para>
/// ⚠ <b>The <c>NO-WAIT</c> semantics do not survive the move to SQL-92 unchanged.</b> ABL's
/// <c>FIND … EXCLUSIVE-LOCK NO-WAIT</c> followed by <c>LOCKED(buffer)</c> asks the database
/// "is another session holding this row this instant?" and answers immediately. SQL has no
/// such predicate. The nearest equivalent is a <c>SELECT … FOR UPDATE</c> with the lock wait
/// set to zero, where a lock-wait timeout <i>is</i> the answer — an exception used as a
/// return value. That is what the implementation does, and it is the least faithful thing in
/// this repository: whether the OpenEdge SQL engine surfaces the timeout as a distinguishable
/// error rather than a generic one has not been tested. Get it on the DBA list with the rest.
/// </para>
/// <para>
/// The consequence of getting it wrong is not symmetric. Reporting "locked" when it is not
/// costs one failed Locus job that will be retried. Reporting "not locked" when it is means
/// deleting a movemst row out from under an operator mid-transaction. Where the answer is
/// unclear, fail closed.
/// </para>
/// </remarks>
public interface IVirtualLockRepository
{
    /// <summary>
    /// Reports whether <paramref name="recordId"/> of <paramref name="tableName"/> is held,
    /// free, or holdable. Converts <c>locusAPI.cls:3322-3332</c>.
    /// </summary>
    /// <param name="tableName">The locked table's name, e.g. <c>"movemst"</c>. Compared case-insensitively.</param>
    /// <param name="recordId">
    /// <c>STRING(ROWID(row))</c> of the locked record — for a movement, <see cref="Movement.RowIdentity"/>.
    /// See the warning there: whether the two string forms agree is unverified.
    /// </param>
    Task<VirtualLockState> GetStateAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the lock row. Converts <c>locusAPI.cls:3336</c>. A no-op when no row exists.
    /// </summary>
    Task<int> ReleaseAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default);
}
