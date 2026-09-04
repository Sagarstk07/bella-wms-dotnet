using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Platform.Audit;

/// <summary>
/// The single append-only writer for the <c>transactions</c> table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 6 §5 and §10 item 3.</b> The <c>n_trans.t</c> CREATE trigger (57 lines) is
/// replaced by application logic in this service. Because a trigger fires for every
/// writer while a service only fires for callers, this is the one contract the whole
/// conversion depends on: <b>every writer must go through here</b>. Twenty-three
/// boundaries currently write <c>transactions</c> directly; if even one keeps a direct
/// write after conversion, the audit trail silently loses rows.
/// </para>
/// <para>
/// Append-only by design. There is no update or delete method, and there will not be one.
/// The single exception in the ABL — <c>locusAPI.cls:3013-3025</c>, which re-finds
/// <c>IG</c> rows <c>EXCLUSIVE-LOCK</c> and overwrites <c>comment</c> with the Locus
/// <c>ExecUser</c> — is a defect being carried, not a pattern to preserve. It is recorded
/// in <c>docs/ABL_CROSSREF.md</c> and the converted path sets the field at creation.
/// </para>
/// </remarks>
public interface IAuditService
{
    /// <summary>
    /// Appends one audit row inside the caller's unit of work.
    /// </summary>
    /// <remarks>
    /// The caller's transaction is used deliberately: an audit row must commit or roll
    /// back with the business change it records. The ABL relied on the trigger firing
    /// inside the same transaction for exactly this reason.
    /// </remarks>
    Task<OperationResult<long>> AppendAsync(
        AuditTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Appends several rows in one round trip, still inside the caller's transaction.</summary>
    Task<OperationResult> AppendManyAsync(
        IReadOnlyCollection<AuditTransaction> transactions,
        CancellationToken cancellationToken = default);
}
