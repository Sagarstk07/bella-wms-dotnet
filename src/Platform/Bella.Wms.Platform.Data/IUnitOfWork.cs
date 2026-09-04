using System.Data;

namespace Bella.Wms.Platform.Data;

/// <summary>
/// Explicit unit of work. Maps one-to-one onto an ABL <c>DO TRANSACTION</c> block.
/// </summary>
/// <remarks>
/// <para>
/// Phase 6 §4: "The unit of work is an explicit object with an explicit lifetime.
/// It is opened by the application service that owns the business operation, and it
/// maps one-to-one onto an ABL <c>DO TRANSACTION</c> block in the code being replaced."
/// </para>
/// <para>
/// <c>api/wms</c> contains 50 <c>DO TRANSACTION</c> blocks and 58 <c>EXCLUSIVE-LOCK</c>
/// sites. The transaction is never implicit here — ABL's outermost-updating-block rule
/// has no .NET equivalent, and Phase 2 §3 warns that most ABL transaction boundaries
/// are implicit. Every boundary in converted code is therefore re-derived by reading
/// the ABL, not inherited.
/// </para>
/// <para>
/// <b>Rule 1 (Phase 6 §4): a transaction never spans a boundary.</b> If an operation
/// needs two modules to change state, it is two transactions and a compensating action.
/// </para>
/// <para>
/// <b>Rule 3: no lock is held across an I/O call.</b> The existing code violates this —
/// <c>locusAPI.cls:2954</c> runs <c>wms_webhost/wspick.p</c> persistently inside an open
/// <c>PICK-BLOCK</c> transaction, and <c>accutechAutoBaggerOut.cls</c> calls carrier
/// APIs inside transaction scope. Converted code moves those calls outside, and each
/// instance is recorded as a deliberate behavioural difference.
/// </para>
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>The connection enlisted in this unit of work.</summary>
    IDbConnection Connection { get; }

    /// <summary>The ambient transaction, or <see langword="null"/> outside a transaction.</summary>
    IDbTransaction? Transaction { get; }

    /// <summary>True between <see cref="BeginAsync"/> and commit or rollback.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Opens a transaction. <paramref name="lockOrder"/> declares, for review, the
    /// order in which this operation will take locks on shared tables.
    /// </summary>
    /// <remarks>
    /// Phase 6 §4 rule 2: "Lock ordering is declared, not emergent. Each module
    /// publishes the order in which it acquires locks on shared tables. The order must
    /// match the ABL code it replaces, because both run concurrently for the whole of
    /// Phase 11."
    /// </remarks>
    Task BeginAsync(LockOrder lockOrder, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
