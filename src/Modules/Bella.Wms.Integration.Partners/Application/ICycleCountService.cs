using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// Flags an inventory record for cycle counting. The seam for
/// <c>rf/wsset_cyc.p</c>.
/// </summary>
/// <remarks>
/// <para>
/// The ABL calls this whenever a stock movement drives <c>inventory.total_qty</c> below
/// zero — eight call sites in <c>locusAPI.cls</c> alone. Negative stock means the recorded
/// quantity and the shelf have diverged, and a cycle count is how the warehouse finds out
/// which is right.
/// </para>
/// <para>
/// <b>Why this is a seam and not a conversion.</b> <c>rf/wsset_cyc.p</c> is 259 lines and
/// its first statement is <c>{wms_webhost/rfglobals.i}</c> — the <c>rf/</c> module's shared
/// variable block. Converting it pulls in the 170 shared variables that make <c>rf/</c> the
/// hardest module in the codebase, which is exactly the boundary this module should not
/// cross. It writes <c>cycle_cnt</c> and <c>task</c>, neither of which <c>api/</c> owns.
/// </para>
/// <para>
/// So: declare the contract, stub the implementation, and let the <c>rf/</c> conversion
/// fill it in. Same treatment as <c>ILocusClient</c> and <c>INotificationService</c>.
/// </para>
/// </remarks>
public interface ICycleCountService
{
    /// <summary>
    /// Marks an inventory record for counting. Converts
    /// <c>run rf/wsset_cyc.p(co, wh, emp, inventory.id)</c>.
    /// </summary>
    /// <remarks>
    /// The ABL treats a failure here as fatal to the whole operation — every call site
    /// checks <c>ERROR-STATUS:ERROR</c> and does <c>undo … return</c> with "Error setting
    /// cycle count." Callers should do the same rather than logging and continuing:
    /// stock is already known to be wrong at this point.
    /// </remarks>
    Task<OperationResult> FlagForCountAsync(
        string company,
        string warehouse,
        string employeeNumber,
        int inventoryId,
        CancellationToken cancellationToken = default);
}
