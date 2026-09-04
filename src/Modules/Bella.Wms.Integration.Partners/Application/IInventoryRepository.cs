using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// Reads and writes <c>inventory</c> — what stock is in which bin.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the highest-consequence table in the conversion.</b> Everything else describes
/// work; this one describes what is physically on the shelves. A wrong write here is not a
/// failed request, it is a warehouse that cannot find its stock.
/// </para>
/// <para>
/// Two rules the implementation must not lose:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>bin_num</c> is stored uppercase. That is the only surviving line of <c>w_invent.t</c>,
/// and a trigger applies it to every writer where a repository applies it only to callers.
/// </description></item>
/// <item><description>
/// Negative <c>total_qty</c> is not an error to swallow. It means the record and the shelf
/// disagree, and the ABL responds by flagging a cycle count — see <see cref="ICycleCountService"/>.
/// </description></item>
/// </list>
/// </remarks>
public interface IInventoryRepository
{
    /// <summary>
    /// Finds one inventory record by its physical position: bin, pallet and item. Converts
    /// <c>locusAPI.cls:3348-3355</c> (source, on the robot) and <c>3381-3388</c>
    /// (destination, on the shelf).
    /// </summary>
    /// <param name="palletId">
    /// The pallet, or the empty string. Empty is meaningful, not missing: a primary pick
    /// shelf holds stock split off a pallet and carries no pallet id
    /// (<c>locusAPI.cls:3385</c>).
    /// </param>
    /// <param name="forUpdate">Take an exclusive row lock, as the ABL's <c>EXCLUSIVE-LOCK</c> does.</param>
    /// <remarks>
    /// This is the <c>co_wh_bin_pallet_abs</c> index exactly — and that index is UNIQUE, so
    /// at most one row can match. The ABL's <c>FIND FIRST</c> is therefore not hiding an
    /// ambiguity here, unlike the <c>movemst</c> lookup.
    /// </remarks>
    Task<Inventory?> FindByLocationAsync(
        string company,
        string warehouse,
        string binNumber,
        string palletId,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every inventory record in one bin, regardless of item or pallet. Converts the
    /// <c>FOR EACH inventory</c> at <c>locusAPI.cls:3655-3659</c>, which walks everything a
    /// Locus tote is carrying.
    /// </summary>
    /// <remarks>
    /// Reads only — the ABL takes <c>NO-LOCK</c> here and re-finds each row under an
    /// exclusive lock before touching it.
    /// </remarks>
    Task<IReadOnlyList<Inventory>> FindByBinAsync(
        string company,
        string warehouse,
        string binNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one inventory record by bin and item, <b>ignoring the pallet</b>. Converts
    /// <c>locusAPI.cls:3662-3667</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Not the same lookup as <see cref="FindByLocationAsync"/>, and the difference
    /// matters. That one uses the whole of <c>co_wh_bin_pallet_abs</c>, which is UNIQUE, so
    /// it can match at most one row. This one drops <c>pallet_id</c>, so a bin holding the
    /// same item on two pallets has two matches and the ABL's <c>FIND FIRST</c> takes one of
    /// them — while the surrounding <c>FOR EACH</c> visits both. See the defect note on
    /// <c>LocusPutawayPutHandler</c>.
    /// </remarks>
    Task<Inventory?> FindByBinAndItemAsync(
        string company,
        string warehouse,
        string binNumber,
        string itemNumber,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>date_time</c> on one record. Converts <c>locusAPI.cls:4353-4363</c> — the
    /// FIFO-timestamp copy in <c>releaseReplenishment</c>.
    /// </summary>
    /// <remarks>
    /// <b>This exists because the codebase contains two versions of the same logic and only
    /// one of them works.</b> <c>releaseReplenishment</c> copies the source timestamp in an
    /// explicit block that handles the blank case correctly; <c>replenputcomplete</c> and
    /// <c>putawayput</c> try to do it inside an <c>ASSIGN … WHEN</c> whose condition can
    /// never be true. That the working version exists is the strongest evidence available
    /// that the other two are a defect rather than a decision.
    /// </remarks>
    Task<int> SetDateTimeAsync(
        int id,
        string? dateTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="delta"/> to <c>total_qty</c> and returns the resulting quantity.
    /// Pass a negative value to remove stock. Converts <c>locusAPI.cls:3358</c> and
    /// <c>3456</c>.
    /// </summary>
    /// <returns>
    /// The new <c>total_qty</c>, which the caller must test for a negative — that is the
    /// cycle-count trigger, and the reason this returns the value rather than a row count.
    /// </returns>
    /// <remarks>
    /// Expressed as <c>SET total_qty = total_qty + @delta</c> rather than a read, an add and
    /// a write. That is both closer to the ABL's intent and safer: the arithmetic happens
    /// inside the row lock, so a concurrent adjustment cannot be lost.
    /// </remarks>
    Task<decimal> AdjustQuantityAsync(
        int id,
        decimal delta,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an inventory record and returns its allocated <c>id</c>. Converts
    /// <c>locusAPI.cls:3393-3430</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id comes from <c>ISequenceAllocator</c> against <c>inventory_id</c>, which cycles.
    /// Four columns with ABL defaults that an <c>INSERT</c> would otherwise leave unset are
    /// written explicitly — see <see cref="Inventory"/>.
    /// </para>
    /// </remarks>
    Task<int> CreateAsync(
        Inventory inventory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an inventory record. Converts <c>locusAPI.cls:3510</c>, which removes the
    /// source row once its quantity reaches exactly zero.
    /// </summary>
    /// <remarks>
    /// ⚠ The ABL's condition is <c>EQ 0</c>, not <c>LE 0</c>. A row driven negative is left
    /// in place — deliberately, since the cycle count needs something to count. Callers
    /// should reproduce that test rather than tidying it into "at or below zero".
    /// </remarks>
    Task<int> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);
}
