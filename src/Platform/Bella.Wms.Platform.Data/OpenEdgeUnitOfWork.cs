using System.Data;
using System.Data.Odbc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bella.Wms.Platform.Data;

/// <summary>
/// Connection options for the OpenEdge SQL-92 layer over ODBC.
/// </summary>
/// <remarks>
/// Phase 6 decision 2: the OpenEdge <c>irms</c> and <c>wmscomm</c> databases remain the
/// system of record for the whole conversion. No schema migration; .NET connects through
/// the SQL-92 layer.
/// </remarks>
public sealed class OpenEdgeOptions
{
    public const string SectionName = "OpenEdge";

    /// <summary>ODBC connection string for the <c>irms</c> database.</summary>
    public string IrmsConnectionString { get; init; } = string.Empty;

    /// <summary>ODBC connection string for the <c>wmscomm</c> database.</summary>
    public string WmsCommConnectionString { get; init; } = string.Empty;

    /// <summary>Command timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;
}

/// <summary>Which OpenEdge database a unit of work targets.</summary>
public enum WmsDatabase
{
    /// <summary>The main WMS database.</summary>
    Irms,

    /// <summary>The interface/communication database holding <c>comm_in</c> and <c>comm_out</c>.</summary>
    WmsComm,
}

/// <summary>
/// ODBC-backed <see cref="IUnitOfWork"/> against OpenEdge.
/// </summary>
/// <remarks>
/// Abstract because the database is chosen by type rather than by constructor argument —
/// see <see cref="IIrmsUnitOfWork"/>. A constructor argument would have made this
/// unresolvable by DI without a factory, and would have hidden which database a class
/// touches.
/// </remarks>
public abstract class OpenEdgeUnitOfWork : IUnitOfWork
{
    private readonly ILogger _logger;
    private readonly OdbcConnection _connection;
    private OdbcTransaction? _transaction;
    private LockOrder _lockOrder = LockOrder.None;
    private bool _disposed;

    protected OpenEdgeUnitOfWork(
        IOptions<OpenEdgeOptions> options,
        WmsDatabase database,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var connectionString = database switch
        {
            WmsDatabase.Irms => options.Value.IrmsConnectionString,
            WmsDatabase.WmsComm => options.Value.WmsCommConnectionString,
            _ => throw new ArgumentOutOfRangeException(nameof(database)),
        };

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No ODBC connection string configured for {database}.");
        }

        _connection = new OdbcConnection(connectionString);
    }

    public IDbConnection Connection
    {
        get
        {
            EnsureOpen();
            return _connection;
        }
    }

    public IDbTransaction? Transaction => _transaction;

    public bool IsActive => _transaction is not null;

    public Task BeginAsync(LockOrder lockOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockOrder);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already active. Nested transactions are not supported — " +
                "ABL's outermost-updating-block rule has no .NET equivalent and every " +
                "boundary must be explicit (Phase 6 §4).");
        }

        EnsureOpen();
        _lockOrder = lockOrder;
        _transaction = _connection.BeginTransaction(IsolationPolicy.WriteDefault);

        _logger.LogDebug("Transaction opened. Declared lock order: {LockOrder}", lockOrder);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }

        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;

        _logger.LogDebug("Transaction committed for {Operation}.", _lockOrder.Operation);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction is null)
        {
            return Task.CompletedTask;
        }

        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;

        _logger.LogWarning("Transaction rolled back for {Operation}.", _lockOrder.Operation);
        return Task.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // An open transaction at dispose means an operation exited without deciding.
        // ABL's UNDO would have rolled back; do the same and make the noise loud.
        if (_transaction is not null)
        {
            _logger.LogError(
                "Unit of work disposed with an open transaction for {Operation}. Rolling back.",
                _lockOrder.Operation);
            await RollbackAsync().ConfigureAwait(false);
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }
}

/// <summary>Unit of work against the <c>irms</c> database.</summary>
public sealed class IrmsUnitOfWork : OpenEdgeUnitOfWork, IIrmsUnitOfWork
{
    public IrmsUnitOfWork(IOptions<OpenEdgeOptions> options, ILogger<IrmsUnitOfWork> logger)
        : base(options, WmsDatabase.Irms, logger)
    {
    }
}

/// <summary>Unit of work against the <c>wmscomm</c> database.</summary>
public sealed class WmsCommUnitOfWork : OpenEdgeUnitOfWork, IWmsCommUnitOfWork
{
    public WmsCommUnitOfWork(IOptions<OpenEdgeOptions> options, ILogger<WmsCommUnitOfWork> logger)
        : base(options, WmsDatabase.WmsComm, logger)
    {
    }
}
