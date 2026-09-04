using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// Reads and removes <c>movemst</c> rows — the outstanding stock-move instructions a Locus
/// robot works from.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately read-and-delete. <c>api/</c> never creates a movement: the replenishment is
/// built elsewhere (<c>rf/</c> and the production staging guide) and Locus only reports that
/// one finished. Adding a create here would put move generation behind an integration
/// endpoint, which is the wrong module.
/// </para>
/// </remarks>
public interface IMovementRepository
{
    /// <summary>
    /// Finds the outstanding replenishment for a licence plate sitting on a given robot.
    /// Converts <c>locusAPI.cls:3292-3298</c>.
    /// </summary>
    /// <param name="licencePlate">
    /// The SSCC-18 from the Locus payload, matched against <c>custom_data[1]</c>.
    /// </param>
    /// <param name="robot">The robot id, matched against <c>bin_from</c>.</param>
    /// <param name="forUpdate">
    /// Take an exclusive row lock, as the ABL's <c>EXCLUSIVE-LOCK</c> does. Required when
    /// the caller intends to delete the row.
    /// </param>
    /// <remarks>
    /// <para>
    /// ⚠ <b>This lookup uses no index.</b> The predicate is
    /// <c>co_num, wh_num, custom_data[1], bin_from</c>. <c>movemst</c>'s primary index is
    /// <c>co_num, wh_num, bin_from, abs_num, row_status</c> — <c>custom_data</c> appears in
    /// none of the six indexes, and <c>bin_from</c> is only reachable through it as the
    /// third field. So the database can narrow to the robot and must then scan. That is
    /// tolerable on a small open-move table and expensive on a large one; worth measuring
    /// before the parallel run rather than after.
    /// </para>
    /// <para>
    /// The ABL uses <c>FIND FIRST</c> with no ordering, so where two rows match, which one
    /// comes back is whatever the primary index yields first. Reproduced, not fixed.
    /// </para>
    /// </remarks>
    Task<Movement?> FindReplenishmentAsync(
        string company,
        string warehouse,
        string licencePlate,
        string robot,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an outstanding movement off a tote, by <c>bin_from</c> and a status of
    /// <c>A</c> or <c>O</c>. Converts <c>locusAPI.cls:3641-3647</c>.
    /// </summary>
    /// <remarks>
    /// This is <c>putawayput</c>'s "is this a replenishment?" test. If a movement exists the
    /// whole putaway path is skipped and <c>releaseReplenishment</c> runs instead — one
    /// event, two entirely different behaviours, chosen by the presence of a row.
    /// <br/>
    /// The ABL reads <c>NO-LOCK</c> and then re-finds the row under an exclusive lock inside
    /// <c>releaseReplenishment</c>, so this does not lock.
    /// </remarks>
    Task<Movement?> FindOpenByToteAsync(
        string company,
        string warehouse,
        string tote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one movement by id. Converts <c>locusAPI.cls:4147-4150</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The ABL's <c>FIND</c> here is on <c>id</c> alone, with no company or warehouse
    /// filter — <c>id</c> is UNIQUE across the whole table, so it works, but it means a
    /// wrong id reaches into another warehouse's data rather than finding nothing. The
    /// company and warehouse are required here and applied, which is a deliberate departure:
    /// it can only ever turn a cross-tenant read into a miss.
    /// </remarks>
    Task<Movement?> FindByIdAsync(
        string company,
        string warehouse,
        int id,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="delta"/> to <c>quantity</c> and returns the result. Converts
    /// <c>locusAPI.cls:4171</c>.
    /// </summary>
    Task<decimal> AdjustQuantityAsync(
        int id,
        decimal delta,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one movement by its id. Converts <c>locusAPI.cls:3346</c>.
    /// </summary>
    /// <remarks>
    /// The ABL deletes the row outright once the move is reported complete — it does not set
    /// <c>row_status</c>. The audit trail is the <c>MR</c> transaction written afterwards,
    /// not this table.
    /// </remarks>
    Task<int> DeleteAsync(
        string company,
        string warehouse,
        int id,
        CancellationToken cancellationToken = default);
}
