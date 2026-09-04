namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// The open <c>JT</c> transaction that records which carton a Locus tote is carrying.
/// </summary>
/// <param name="CartonId">The carton the tote is currently working.</param>
/// <param name="IsVoid">
/// <c>transactions.void</c>. <b>Not a deletion marker here.</b> <c>toteinduct</c> assigns
/// <c>transactions.void = lSingleUnit</c> (<c>locusAPI.cls:2327</c>), so on a <c>JT</c> row
/// this flag means "single-unit order", which is what the duplicate-tote check tests.
/// </param>
public readonly record struct ToteTransaction(string? CartonId, bool IsVoid);

/// <summary>
/// The <c>JT</c> transaction rows that track a Locus tote, converting the
/// <c>trans_type = "JT"</c> queries the tote handlers run against <c>transactions</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This deliberately updates the audit table, which is otherwise append-only.</b>
/// <c>IAuditService</c> only ever inserts, on the principle that an audit trail you can
/// edit is not an audit trail. But the ABL genuinely uses <c>transactions</c> as working
/// state for totes: it re-finds <c>JT</c> rows <c>EXCLUSIVE-LOCK</c> and flips
/// <c>row_status</c> to <c>C</c> when a tote is released
/// (<c>locusAPI.cls:2510-2519</c> among others).
/// </para>
/// <para>
/// That is a design smell in the original, not something to fix here — the rows are the
/// system of record for which tote holds which carton, and changing it would need the
/// warehouse team's agreement. Keeping it behind its own narrow interface at least makes
/// the exception visible instead of quietly widening <c>IAuditService</c>.
/// </para>
/// <para>
/// <c>row_status</c> on a <c>JT</c> row runs <c>P</c> → <c>O</c> → <c>C</c>
/// (<c>locusAPI.cls:2242</c> notes "can be P, O or C"), a wider range than the
/// <c>O</c>/<c>C</c>/<c>E</c> the reject family deals with.
/// </para>
/// </remarks>
public interface IToteTransactionRepository
{
    /// <summary>
    /// Finds the open <c>JT</c> row for a tote, if there is one — the lookup
    /// <c>toteinduct</c> and <c>totemove</c> use to detect a tote that is still in use.
    /// </summary>
    /// <remarks>
    /// The ABL uses <c>find first</c>, then re-runs the same query with a bare
    /// <c>find</c> to detect the multi-row case. Returning at most one row here and letting
    /// the caller close all matches achieves the same outcome without the double query.
    /// </remarks>
    Task<ToteTransaction?> FindOpenByToteAsync(
        string company,
        string warehouse,
        string toteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes every open <c>JT</c> row for a tote — sets <c>row_status</c> to <c>C</c> and
    /// stamps <paramref name="comments"/>.
    /// </summary>
    /// <param name="cartonId">
    /// When supplied, restricts the close to rows for that carton, which is what
    /// <c>toteinductcancel</c> does (<c>locusAPI.cls:2507</c>). <c>toteinduct</c> passes
    /// nothing and closes every open row for the tote.
    /// </param>
    /// <returns>How many rows were closed. Zero is normal.</returns>
    Task<int> CloseOpenAsync(
        string company,
        string warehouse,
        string toteId,
        string? cartonId,
        string comments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Points every open <c>JT</c> row for an order at a different tote. Converts the
    /// final loop of <c>totemove</c> (<c>locusAPI.cls:2446-2461</c>), which is what
    /// actually moves the tote: the rows keep their carton and change their
    /// <c>link_id</c>.
    /// </summary>
    /// <remarks>
    /// <b>Selected by order, not by carton.</b> The filter is
    /// <c>po_number</c> + <c>po_suffix</c> + <c>row_status = "O"</c> + <c>trans_type =
    /// "JT"</c>, so a move sweeps up every carton on the order rather than just the one
    /// named in the request. That is the ABL's behaviour and it is presumably why a move
    /// exists as a separate event from an induct.
    /// <br/>
    /// Note the status test here is <c>eq "O"</c> — stricter than the <c>ne "C"</c> used
    /// everywhere else in this interface, so a row sitting at <c>P</c> is skipped.
    /// </remarks>
    Task<int> RelinkOpenByOrderAsync(
        string company,
        string warehouse,
        string order,
        string orderSuffix,
        string newToteId,
        CancellationToken cancellationToken = default);
}
