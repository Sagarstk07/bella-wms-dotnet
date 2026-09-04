using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>Read access to <c>cartonmst</c>, the table <c>api/wms</c> touches most (29 references).</summary>
public interface ICartonRepository
{
    /// <summary>
    /// Converts the repeated <c>find first cartonmst where co_num / wh_num / carton_id
    /// no-lock</c> pattern — for example <c>locusAPI.cls:1767-1772</c>.
    /// </summary>
    Task<Carton?> FindByCartonIdAsync(
        string company,
        string warehouse,
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the AutoBagger tracking fields. <c>cartonmst.external_status</c> is
    /// <c>x(8)</c> and <c>cartonmst.external_id</c> is <c>x(10)</c> per
    /// <c>accutechAutoBagger.i</c> — the constants defined there must fit those widths.
    /// </summary>
    Task UpdateExternalStatusAsync(
        string company,
        string warehouse,
        string cartonId,
        string externalStatus,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the reject family's "re-find <c>EXCLUSIVE-LOCK</c>, then if
    /// <c>row_status</c> is not already <c>C</c> set it to <c>E</c>" step — for example
    /// <c>locusAPI.cls:1803-1877</c> (<c>reject</c>). A single conditional
    /// <c>UPDATE ... WHERE row_status &lt;&gt; 'C'</c> replaces the ABL's separate
    /// re-find-then-check-then-write, which is both simpler and closes the race the
    /// ABL's two-step version leaves open between its own re-find and the caller's
    /// earlier <c>NO-LOCK</c> idempotency check.
    /// </summary>
    /// <remarks>
    /// Must run inside the caller's transaction, same as
    /// <see cref="UpdateExternalStatusAsync"/>. Zero rows affected is an expected,
    /// non-error outcome here — it means <c>row_status</c> was already <c>C</c>.
    /// </remarks>
    Task MarkErrorUnlessCompleteAsync(
        string company,
        string warehouse,
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an order counts as a "single unit" order for tote handling. Converts the
    /// <c>SINGLEUNITBLOCK</c> loop at <c>locusAPI.cls:2386-2410</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ABL walks every carton on the order and gives up as soon as either the carton
    /// count gets too high or a carton's <c>cartondtl</c> row is missing or has
    /// <c>qty &gt; 1</c>.
    /// </para>
    /// <para>
    /// ⚠ <b>Suspected defect, reproduced not fixed.</b> The count is tested <i>before</i>
    /// it is incremented — <c>if iCartonCount gt 1</c> then <c>iCartonCount = iCartonCount
    /// + 1</c> — so the loop only bails on the <b>third</b> carton. An order with two
    /// cartons is therefore still treated as "single unit", which the name says it should
    /// not be. The flag decides whether a duplicate tote is rejected, so getting it wrong
    /// means either blocking a legitimate induct or allowing a clashing one. Raise it for
    /// the captured-traffic review; do not quietly change it.
    /// </para>
    /// </remarks>
    Task<bool> IsSingleUnitOrderAsync(
        string company,
        string warehouse,
        string order,
        string orderSuffix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The item number on a carton's <b>sole</b> detail line, or <see langword="null"/>
    /// when the carton has none — or more than one. Converts
    /// <c>find cartondtl where carton_num eq …</c> at <c>locusAPI.cls:2331</c>.
    /// </summary>
    /// <remarks>
    /// <b>"More than one" deliberately returns null.</b> The ABL uses a bare <c>find</c>,
    /// not <c>find first</c>, which raises an ambiguity error when several rows match;
    /// <c>NO-ERROR</c> then leaves the buffer unavailable, and <c>if available cartondtl</c>
    /// is false. So a multi-line carton takes exactly the same path as a carton with no
    /// lines at all, and <c>transactions.abs_num</c> is simply left unset.
    /// <br/>
    /// <c>cartondtl</c>'s primary index is <c>(carton_num, abs_num)</c>, so multi-line
    /// cartons are ordinary. Changing this to "first line wins" would look like a fix and
    /// would start stamping item numbers on audit rows that have never carried one.
    /// </remarks>
    Task<string?> FindSoleDetailItemAsync(
        int cartonNumber,
        CancellationToken cancellationToken = default);
}
