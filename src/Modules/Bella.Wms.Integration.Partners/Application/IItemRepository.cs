using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// Read-only access to the two <c>item</c> fields the putaway path needs.
/// </summary>
/// <remarks>
/// No writes, ever. <c>item</c> is the item-master module's table; this module reads two
/// fields off it and nothing more. If something here ever needs to write one, that is a
/// signal the work belongs in a different module, not that this interface should grow.
/// </remarks>
public interface IItemRepository
{
    /// <summary>
    /// Finds an item, or <see langword="null"/>. Converts <c>locusAPI.cls:3473-3477</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The ABL scopes this by <c>static_connect:company</c> and
    /// <c>static_connect:warehouse</c> — the ambient context — while the surrounding method
    /// uses its own <c>cCompany</c>/<c>cWarehouse</c> everywhere else. The two are normally
    /// the same. They are not guaranteed to be, and the ABL does not check.
    /// </remarks>
    Task<Item?> FindAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the item's case quantity, or <b>1</b> when no item row exists. Converts
    /// <c>static_connect:get_case_qty</c> (<c>wms_webhost/static_connect.cls:2706-2727</c>).
    /// </summary>
    /// <remarks>
    /// The fallback is 1 and that is load-bearing, not a tidy-up: a case quantity of 0 would
    /// make anything dividing by it fail. Reproduced exactly.
    /// </remarks>
    Task<int> GetCaseQuantityAsync(
        string company,
        string warehouse,
        string itemNumber,
        CancellationToken cancellationToken = default);
}
