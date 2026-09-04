namespace Bella.Wms.Platform.Data;

/// <summary>
/// Draws the next unused value from an OpenEdge sequence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just "SELECT NEXTVAL".</b> <b>51 of the 55 sequences</b> in
/// <c>irms.df</c> are declared <c>CYCLE-ON-LIMIT yes</c> with <c>MAX-VAL 999999999</c>.
/// They wrap back to their minimum rather than failing, so a value can be re-issued while
/// a row still holds it — and the columns they feed carry UNIQUE indexes. Every ABL
/// CREATE trigger that assigns an id therefore loops: draw, check, draw again.
/// </para>
/// <para>
/// That is a property of this database, not of any one table. <c>transactions_trans_num</c>,
/// <c>inventory_id</c> and <c>movemst_id</c> all behave this way.
/// </para>
/// </remarks>
public interface ISequenceAllocator
{
    /// <summary>
    /// Draws the next value from <paramref name="sequenceName"/> that no row in
    /// <paramref name="table"/> currently holds in <paramref name="column"/>.
    /// </summary>
    /// <param name="sequenceName">Bare sequence name, e.g. <c>transactions_trans_num</c>.</param>
    /// <param name="table">Unqualified table name, e.g. <c>transactions</c>.</param>
    /// <param name="column">The column carrying the value, e.g. <c>trans_num</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// No transaction is active, or no free value was found within the retry limit.
    /// </exception>
    Task<int> NextAsync(
        string sequenceName,
        string table,
        string column,
        CancellationToken cancellationToken = default);
}
