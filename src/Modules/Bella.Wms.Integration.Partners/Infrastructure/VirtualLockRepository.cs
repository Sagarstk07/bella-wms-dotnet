using System.Data.Common;
using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Infrastructure;

/// <summary>
/// Dapper implementation of <see cref="IVirtualLockRepository"/> over
/// <c>PUB.virtual_lock</c>.
/// </summary>
/// <remarks>
/// <para>
/// Columns verified against <c>schema/irms.df</c>, 2026-09-04.
/// </para>
/// <para>
/// ⚠ <b>This is the least faithful class in the module, and it says so on purpose.</b> The
/// ABL asks the database a question SQL cannot ask — "is another session holding this row
/// right now?" — and gets an immediate yes or no. Here that becomes a locking read whose
/// timeout is treated as "yes". Every part of that is a guess until it runs against a real
/// database: whether <c>FOR UPDATE</c> is accepted, whether the driver has a settable lock
/// wait, and whether a lock timeout is distinguishable from any other failure.
/// </para>
/// <para>
/// So the class fails closed. Anything it cannot interpret is reported as
/// <see cref="VirtualLockState.HeldByAnotherSession"/>, which costs a retried Locus job. The
/// opposite mistake deletes a movemst row out from under an operator.
/// </para>
/// </remarks>
public sealed class VirtualLockRepository : IVirtualLockRepository
{
    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<VirtualLockRepository> _logger;

    public VirtualLockRepository(
        IIrmsUnitOfWork unitOfWork,
        ILogger<VirtualLockRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<VirtualLockState> GetStateAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (string.IsNullOrWhiteSpace(recordId))
        {
            // No key means nothing to look up. The ABL would find nothing too — but it
            // would get there having computed a real ROWID, so a blank here means the
            // caller failed to read one and that is worth surfacing rather than reporting
            // a clean "not locked".
            throw new ArgumentException(
                "A virtual_lock lookup needs the locked row's identity. An empty value " +
                "usually means ROWID was not selected — see Movement.RowIdentity.",
                nameof(recordId));
        }

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Checking a virtual_lock must run inside the unit of work that will act on " +
                "the answer. Outside it, the row can be locked between the check and the act.");
        }

        // tablename is compared case-insensitively: the ABL passes "movemst" here and
        // "ORDHDR" at remote/ship-change-b.p:1173, so both cases are genuinely present in
        // this column and ABL comparison never distinguished them. Two literals rather
        // than UPPER(), to keep the row_key index usable.
        const string Sql = """
            SELECT tablename
            FROM   PUB.virtual_lock
            WHERE  tablename IN (@lower, @upper)
            AND    record_id = @recordId
            FOR UPDATE
            """;

        try
        {
            var found = await _unitOfWork.Connection.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    Sql,
                    new
                    {
                        lower = tableName.ToLowerInvariant(),
                        upper = tableName.ToUpperInvariant(),
                        recordId,
                    },
                    transaction: _unitOfWork.Transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return found is null ? VirtualLockState.NotLocked : VirtualLockState.Releasable;
        }
        catch (DbException ex)
        {
            // Converts `if locked(virtual_lock)` — locusAPI.cls:3328. A lock wait that
            // fails is the only signal SQL gives that another session holds the row.
            //
            // ⚠ This catch is wider than it should be. It cannot yet distinguish a lock
            // timeout from a missing table, a permission error or a syntax mistake, because
            // nobody has seen what the OpenEdge ODBC driver reports for any of them. Once
            // that is known, narrow this to the timeout SQLSTATE and let the rest throw.
            _logger.LogWarning(
                ex,
                "Treating a database error on the virtual_lock read for {Table}/{RecordId} " +
                "as 'held by another session'. This catch is deliberately over-broad until " +
                "the driver's lock-timeout SQLSTATE is known — see the class remarks.",
                tableName, recordId);

            return VirtualLockState.HeldByAnotherSession;
        }
    }

    /// <inheritdoc />
    public async Task<int> ReleaseAsync(
        string tableName,
        string recordId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        if (!_unitOfWork.IsActive)
        {
            throw new InvalidOperationException(
                "Releasing a virtual_lock must run inside a unit of work. The ABL deletes " +
                "it inside the same transaction that deletes the record it guards, so a " +
                "rollback puts the lock back.");
        }

        return await _unitOfWork.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM PUB.virtual_lock
                WHERE  tablename IN (@lower, @upper)
                AND    record_id = @recordId
                """,
                new
                {
                    lower = tableName.ToLowerInvariant(),
                    upper = tableName.ToUpperInvariant(),
                    recordId,
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
