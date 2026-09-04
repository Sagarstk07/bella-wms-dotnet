using System.Data;

namespace Bella.Wms.Platform.Data;

/// <summary>
/// Enforces the read-isolation rule that keeps .NET clients from blocking ABL pickers
/// on the warehouse floor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 6 §2, the constraint that governs coexistence.</b> ABL's default lock is
/// <c>SHARE-LOCK</c>, not <c>NO-LOCK</c>. Any <c>FIND</c> or <c>FOR EACH</c> in the
/// existing code that does not say <c>NO-LOCK</c> holds a share lock for the enclosing
/// transaction, and an <c>EXCLUSIVE-LOCK</c> attempt fails while any other session holds
/// a share lock on that record. So a .NET client reading at an isolation level that takes
/// read locks can block ABL pickers.
/// </para>
/// <para>The rules, from Phase 6 §2:</para>
/// <list type="bullet">
///   <item>Every .NET read that does not intend to write <b>must</b> run at <c>READ UNCOMMITTED</c>.</item>
///   <item>Every .NET write path must take its locks in the same order the ABL takes them.</item>
///   <item>Read-committed or higher is opt-in, per query, with a comment justifying it.</item>
/// </list>
/// <para>
/// <b>Unverified assumption.</b> Phase 6 §12 test 1 — whether the OpenEdge SQL engine and
/// the ABL client interoperate at the record-lock level in this configuration — has not
/// been run. This class is written as though the answer is yes. If it is no, the database
/// strategy changes and so does this file.
/// </para>
/// </remarks>
public static class IsolationPolicy
{
    /// <summary>
    /// The default for all reads. Corresponds to ABL <c>NO-LOCK</c>.
    /// </summary>
    public const IsolationLevel ReadDefault = IsolationLevel.ReadUncommitted;

    /// <summary>
    /// The default for write transactions. Corresponds to the pessimistic
    /// <c>EXCLUSIVE-LOCK</c> semantics the ABL relies on; the code uses
    /// <c>EXCLUSIVE-LOCK</c> 3,039 times across the codebase and <c>SHARE-LOCK</c> once.
    /// </summary>
    public const IsolationLevel WriteDefault = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Opt out of <see cref="ReadDefault"/> for one query. The justification is required
    /// and is surfaced in review; a read that escalates isolation without one is a bug.
    /// </summary>
    public static IsolationLevel EscalatedRead(IsolationLevel level, string justification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);

        if (level == IsolationLevel.ReadUncommitted)
        {
            throw new ArgumentException(
                "EscalatedRead is for opting *out* of READ UNCOMMITTED. Use ReadDefault instead.",
                nameof(level));
        }

        return level;
    }
}
