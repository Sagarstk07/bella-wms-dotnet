using Bella.Wms.Integration.Partners.Domain;

namespace Bella.Wms.Integration.Partners.Application;

/// <summary>Access to <c>pick</c> for the Locus tote handlers.</summary>
public interface IPickRepository
{
    /// <summary>
    /// The picks belonging to one carton, converting
    /// <c>for each pick where co_num / wh_num / carton_id</c> — the loop all three tote
    /// handlers open (<c>locusAPI.cls:2296</c>, <c>2430</c>, <c>2521</c>).
    /// </summary>
    /// <param name="openPicksOnly">
    /// <see langword="true"/> adds the <c>pick_status</c> filter that <c>toteinduct</c>
    /// applies and the other two do not — see <see cref="SetToteAssignmentAsync"/>.
    /// </param>
    Task<IReadOnlyList<Pick>> FindByCartonAsync(
        string company,
        string warehouse,
        string cartonId,
        bool openPicksOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the robot and tote ids onto every pick for a carton, converting the
    /// re-find-EXCLUSIVE-LOCK-and-assign loop the three tote handlers share.
    /// </summary>
    /// <param name="jobRobot">Goes to <c>custom_data[4]</c>. Empty string clears it.</param>
    /// <param name="toteId">Goes to <c>custom_data[5]</c>. Empty string clears it.</param>
    /// <param name="openPicksOnly">
    /// <see langword="true"/> for <c>toteinduct</c>, which filters on
    /// <c>pick.pick_status eq "o"</c> (<c>locusAPI.cls:2300</c>).
    /// <see langword="false"/> for <c>totemove</c> and <c>toteinductcancel</c>, which do
    /// not filter at all (<c>2430</c>, <c>2521</c>).
    /// <b>The asymmetry is real</b> — induct only claims picks still open, while a move or
    /// a cancel applies to every pick on the carton whatever its state. Preserve it.
    /// </param>
    /// <returns>How many pick rows were written. Zero is possible and not an error.</returns>
    /// <remarks>
    /// One <c>UPDATE</c> replaces the ABL's read-then-re-find-then-write loop. The ABL
    /// re-finds each row <c>EXCLUSIVE-LOCK</c> by rowid after reading it <c>NO-LOCK</c>,
    /// which leaves a window between the two; a single guarded statement closes it, the
    /// same simplification <c>ICartonRepository.MarkErrorUnlessCompleteAsync</c> makes.
    /// <br/>
    /// Must run inside the caller's unit of work.
    /// </remarks>
    Task<int> SetToteAssignmentAsync(
        string company,
        string warehouse,
        string cartonId,
        string jobRobot,
        string toteId,
        bool openPicksOnly,
        CancellationToken cancellationToken = default);
}
