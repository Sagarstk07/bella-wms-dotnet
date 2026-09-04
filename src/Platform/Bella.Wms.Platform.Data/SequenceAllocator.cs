using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Data;

/// <summary>
/// Draws sequence values inside the caller's unit of work, retrying past collisions the
/// way every ABL CREATE trigger in this system does.
/// </summary>
/// <remarks>
/// <para>
/// The reference conversion is <c>trigger/n_trans.t:20-25</c>:
/// </para>
/// <code>
/// tryloop: do while true:
///     try = NEXT-VALUE( transactions_trans_num ).
///     IF NOT CAN-FIND( transactions WHERE transactions.trans_num EQ try ) THEN LEAVE tryloop.
/// end.
/// </code>
/// <para>
/// <b>The one departure:</b> the ABL loops forever; this gives up after
/// <see cref="MaxAttempts"/>. An unbounded retry against a fully-consumed sequence spins
/// while holding an open transaction — on <c>transactions</c> or <c>inventory</c>, that
/// blocks every writer in the warehouse. Exhausting a million values means something is
/// wrong that retrying will not fix.
/// </para>
/// </remarks>
public sealed class SequenceAllocator : ISequenceAllocator
{
    /// <summary>Draws to attempt before failing. The ABL retries indefinitely.</summary>
    public const int MaxAttempts = 50;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<SequenceAllocator> _logger;

    public SequenceAllocator(IIrmsUnitOfWork unitOfWork, ILogger<SequenceAllocator> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> NextAsync(
        string sequenceName,
        string table,
        string column,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        if (!_unitOfWork.IsActive)
        {
            // Outside the caller's transaction, a rollback would leave a gap in the
            // sequence and another writer could take the value between check and insert.
            throw new InvalidOperationException(
                $"A value from {sequenceName} must be allocated inside the unit of work " +
                "that will write the row. Call BeginAsync first.");
        }

        // These three are interpolated, not parameterised, because they are identifiers
        // rather than values — SQL will not bind them. They come from constants in this
        // solution, never from a request; the guard below keeps it that way.
        Validate(sequenceName);
        Validate(table);
        Validate(column);

        // ⚠ UNVERIFIED. OpenEdge exposes sequences to SQL-92 as <sequence>.NEXTVAL selected
        // from SYSPROGRESS.SYSCALCTABLE, its single-row system table (the equivalent of
        // Oracle's DUAL). Never run against a real connection — first thing to check when
        // the database is available. docs/SCHEMA_REVIEW.md item 4.
        var nextValueSql = $"SELECT PUB.{sequenceName}.NEXTVAL FROM SYSPROGRESS.SYSCALCTABLE";
        var isTakenSql = $"SELECT COUNT(*) FROM PUB.{table} WHERE {column} = @candidate";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = await _unitOfWork.Connection
                .ExecuteScalarAsync<int>(new CommandDefinition(
                    nextValueSql,
                    transaction: _unitOfWork.Transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var taken = await _unitOfWork.Connection
                .ExecuteScalarAsync<int>(new CommandDefinition(
                    isTakenSql,
                    new { candidate },
                    transaction: _unitOfWork.Transaction,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (taken == 0)
            {
                if (attempt > 1)
                {
                    // Means the sequence has wrapped and rows from the previous cycle
                    // are still present. Worth knowing before it becomes a support call.
                    _logger.LogWarning(
                        "Allocated {Sequence} = {Value} after {Attempts} attempts. The " +
                        "sequence has cycled past rows that still exist in {Table}.",
                        sequenceName, candidate, attempt, table);
                }

                return candidate;
            }

            _logger.LogDebug(
                "{Sequence} returned {Candidate}, already present in {Table}.{Column}; drawing again.",
                sequenceName, candidate, table, column);
        }

        throw new InvalidOperationException(
            $"Could not allocate a free {table}.{column} from {sequenceName} in " +
            $"{MaxAttempts} attempts. The sequence cycles at its maximum and every value " +
            "drawn is already in use, which means the table needs archiving or the " +
            "sequence needs resetting. See trigger/n_trans.t and docs/SCHEMA_REVIEW.md.");
    }

    /// <summary>
    /// Rejects anything that is not a bare identifier. These values are interpolated into
    /// SQL, so this is the guard that keeps that safe even if a caller is careless.
    /// </summary>
    private static void Validate(string identifier)
    {
        foreach (var c in identifier)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"'{identifier}' is not a bare SQL identifier. Sequence, table and " +
                    "column names are interpolated into the statement and must come from " +
                    "constants, never from request data.");
            }
        }
    }
}
