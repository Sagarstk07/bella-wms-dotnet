using System.Collections.Immutable;

namespace Bella.Wms.Platform.Data;

/// <summary>
/// A declared, ordered list of tables an operation will lock, in acquisition order.
/// </summary>
/// <remarks>
/// <para>
/// Phase 6 §4 rule 2 and Phase 6 §11: "Deadlock between stacks is likely under parallel
/// run unless lock ordering matches." Because ABL and .NET write the same OpenEdge
/// database for the whole of Phases 8-11, a converted operation that takes locks in a
/// different order than the ABL it replaces will deadlock against the ABL under load.
/// </para>
/// <para>
/// Declaring the order in code makes it reviewable against the ABL source, and lets a
/// test assert that two operations touching the same tables agree on order.
/// </para>
/// </remarks>
public sealed class LockOrder
{
    private LockOrder(string operation, ImmutableArray<string> tables, string ablSource)
    {
        Operation = operation;
        Tables = tables;
        AblSource = ablSource;
    }

    /// <summary>Human-readable name of the business operation.</summary>
    public string Operation { get; }

    /// <summary>Tables in the order locks are acquired.</summary>
    public ImmutableArray<string> Tables { get; }

    /// <summary>
    /// The ABL file and line range this order was read from, so a reviewer can check it.
    /// </summary>
    public string AblSource { get; }

    /// <summary>
    /// Declares a lock order. <paramref name="ablSource"/> is required: an order that
    /// cannot be traced to the ABL it replaces has not been verified against anything.
    /// </summary>
    public static LockOrder Declare(string operation, string ablSource, params string[] tables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(ablSource);
        ArgumentNullException.ThrowIfNull(tables);

        return new LockOrder(operation, [.. tables], ablSource);
    }

    /// <summary>
    /// For read-only work that takes no locks. Reads run at <c>READ UNCOMMITTED</c> —
    /// see <see cref="IsolationPolicy"/>.
    /// </summary>
    public static LockOrder None { get; } =
        new("(read-only)", ImmutableArray<string>.Empty, "n/a");

    /// <summary>
    /// True when this order is a prefix-compatible ordering with <paramref name="other"/>:
    /// the tables they share appear in the same relative order in both. Two operations
    /// that fail this check can deadlock against each other.
    /// </summary>
    public bool IsCompatibleWith(LockOrder other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var shared = Tables.Where(t => other.Tables.Contains(t, StringComparer.OrdinalIgnoreCase))
                           .ToArray();

        var otherShared = other.Tables
            .Where(t => Tables.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return shared.SequenceEqual(otherShared, StringComparer.OrdinalIgnoreCase);
    }

    public override string ToString() =>
        Tables.IsEmpty
            ? $"{Operation}: no locks"
            : $"{Operation}: {string.Join(" -> ", Tables)}  [{AblSource}]";
}
