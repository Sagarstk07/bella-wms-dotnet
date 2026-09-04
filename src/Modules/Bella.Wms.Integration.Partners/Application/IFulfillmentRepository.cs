namespace Bella.Wms.Integration.Partners.Application;

/// <summary>
/// Closes the <c>FM</c> (fulfillment) transaction that a movement was opened against.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IToteTransactionRepository"/>, which does the same shape of
/// work for <c>JT</c> rows, because the two are selected differently: a tote row is found by
/// tote and carton, an <c>FM</c> row by the movement's id in <c>link_id</c>. Merging them
/// would produce one method with two mutually exclusive sets of arguments.
/// </para>
/// <para>
/// <b>This is an update to <c>transactions</c>, which is otherwise append-only.</b> The
/// architecture rule that every write goes through <see cref="Bella.Wms.Platform.Audit.IAuditService"/>
/// is about <i>inserts</i> — it replaces the <c>n_trans.t</c> CREATE trigger. Closing an
/// existing row is a different operation and the ABL does it in three places. Worth stating
/// so nobody reads this as a way around the audit service.
/// </para>
/// </remarks>
public interface IFulfillmentRepository
{
    /// <summary>
    /// Marks the open <c>FM</c> transaction for <paramref name="movementId"/> as complete.
    /// Converts <c>locusAPI.cls:3308-3320</c>.
    /// </summary>
    /// <returns>Rows affected. Zero is normal — see the remarks.</returns>
    /// <remarks>
    /// <para>
    /// The ABL's <c>FIND FIRST … NO-ERROR</c> is followed by <c>IF AVAILABLE</c>, so a
    /// missing row is not an error and the method carries on. Reproduced: this returns 0
    /// rather than throwing.
    /// </para>
    /// <para>
    /// The join is <c>link_id EQ STRING(movemst.id)</c> — an <c>integer</c> compared against
    /// a <c>character x(30)</c>. The id is converted to its plain decimal string with no
    /// padding, which is what ABL's <c>STRING()</c> produces for an integer with no format.
    /// </para>
    /// </remarks>
    Task<int> CloseForMovementAsync(
        string company,
        string warehouse,
        int movementId,
        CancellationToken cancellationToken = default);
}
