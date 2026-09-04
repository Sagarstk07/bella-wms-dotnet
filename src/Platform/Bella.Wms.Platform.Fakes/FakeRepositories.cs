using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Bella.Wms.Platform.Identity;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Fakes;

/// <summary>
/// A unit of work that tracks transaction state but touches no database.
/// </summary>
/// <remarks>
/// The state tracking matters: <see cref="AuditService"/> refuses to write outside an
/// open transaction, so a fake that ignored <see cref="BeginAsync"/> would let a bug
/// through that the real implementation catches. Commit and rollback counts are exposed
/// so tests can assert the transaction actually closed.
/// </remarks>
public sealed class FakeUnitOfWork : IIrmsUnitOfWork, IWmsCommUnitOfWork
{
    private readonly ILogger<FakeUnitOfWork> _logger;

    public FakeUnitOfWork(ILogger<FakeUnitOfWork> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Always throws — nothing in fake mode may execute SQL.</summary>
    public System.Data.IDbConnection Connection =>
        throw new InvalidOperationException(
            "FakeUnitOfWork has no connection. Something tried to run SQL in fake mode — " +
            "which means a repository was not replaced by its fake.");

    public System.Data.IDbTransaction? Transaction => null;

    public bool IsActive { get; private set; }

    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public LockOrder? LastLockOrder { get; private set; }

    public Task BeginAsync(LockOrder lockOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockOrder);

        if (IsActive)
        {
            throw new InvalidOperationException(
                "A transaction is already active. The real unit of work rejects this too.");
        }

        IsActive = true;
        LastLockOrder = lockOrder;
        _logger.LogDebug("[fake] transaction opened: {LockOrder}", lockOrder);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }

        IsActive = false;
        CommitCount++;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (IsActive)
        {
            IsActive = false;
            RollbackCount++;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Hands out sequential numbers with no database. Records what was asked for, so a test
/// can assert the right sequence was drawn.
/// </summary>
/// <remarks>
/// The real allocator retries past collisions because 51 of the 55 sequences in this
/// database cycle. Nothing cycles here — the fake exists so handlers can run, not to
/// reproduce sequence exhaustion.
/// </remarks>
public sealed class FakeSequenceAllocator : ISequenceAllocator
{
    private int _next;

    public List<(string Sequence, string Table, string Column)> Draws { get; } = [];

    public Task<int> NextAsync(
        string sequenceName, string table, string column,
        CancellationToken cancellationToken = default)
    {
        Draws.Add((sequenceName, table, column));
        return Task.FromResult(Interlocked.Increment(ref _next));
    }
}

/// <summary><see cref="ICartonRepository"/> over <see cref="InMemoryWarehouse"/>.</summary>
public sealed class FakeCartonRepository : ICartonRepository
{
    private readonly InMemoryWarehouse _warehouse;

    public FakeCartonRepository(InMemoryWarehouse warehouse)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
    }

    public Task<Carton?> FindByCartonIdAsync(
        string company, string warehouse, string cartonId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.FindCarton(company, warehouse, cartonId));

    public Task UpdateExternalStatusAsync(
        string company, string warehouse, string cartonId,
        string externalStatus, string externalId, CancellationToken cancellationToken = default)
    {
        _warehouse.UpdateCartonExternalStatus(company, warehouse, cartonId, externalStatus, externalId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Added 2026-09-01, when this project was first wired into the build. It had drifted
    /// behind <see cref="ICartonRepository"/>: the reject family added
    /// <c>MarkErrorUnlessCompleteAsync</c> and nothing referenced this assembly, so
    /// nothing compiled it and nothing noticed.
    /// </summary>
    public Task MarkErrorUnlessCompleteAsync(
        string company, string warehouse, string cartonId, CancellationToken cancellationToken = default)
    {
        // The return value is deliberately discarded. Zero rows affected means the carton
        // was already "C", which the interface documents as an expected, non-error
        // outcome — the real repository does not surface it either.
        _ = _warehouse.MarkCartonErrorUnlessComplete(company, warehouse, cartonId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The fake has no <c>cartondtl</c>, so it answers from a flag set on the warehouse
    /// rather than counting rows. Enough to exercise both branches of the duplicate-tote
    /// guard; not a substitute for the real query.
    /// </summary>
    public Task<bool> IsSingleUnitOrderAsync(
        string company, string warehouse, string order, string orderSuffix,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.IsSingleUnitOrder(company, warehouse, order, orderSuffix));

    public Task<string?> FindSoleDetailItemAsync(
        int cartonNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.FindSoleDetailItem(cartonNumber));
}

/// <summary><see cref="IPickRepository"/> over <see cref="InMemoryWarehouse"/>.</summary>
public sealed class FakePickRepository : IPickRepository
{
    private readonly InMemoryWarehouse _warehouse;

    public FakePickRepository(InMemoryWarehouse warehouse)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
    }

    public Task<IReadOnlyList<Pick>> FindByCartonAsync(
        string company, string warehouse, string cartonId,
        bool openPicksOnly = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.FindPicksByCarton(company, warehouse, cartonId, openPicksOnly));

    public Task<int> SetToteAssignmentAsync(
        string company, string warehouse, string cartonId,
        string jobRobot, string toteId, bool openPicksOnly,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.SetToteAssignment(
            company, warehouse, cartonId, jobRobot, toteId, openPicksOnly));
}

/// <summary><see cref="IToteTransactionRepository"/> over <see cref="InMemoryWarehouse"/>.</summary>
public sealed class FakeToteTransactionRepository : IToteTransactionRepository
{
    private readonly InMemoryWarehouse _warehouse;

    public FakeToteTransactionRepository(InMemoryWarehouse warehouse)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
    }

    public Task<ToteTransaction?> FindOpenByToteAsync(
        string company, string warehouse, string toteId,
        CancellationToken cancellationToken = default)
    {
        var row = _warehouse.FindOpenToteTransaction(company, warehouse, toteId);
        return Task.FromResult(row is null
            ? (ToteTransaction?)null
            : new ToteTransaction(row.Value.CartonId, row.Value.IsVoid));
    }

    public Task<int> CloseOpenAsync(
        string company, string warehouse, string toteId, string? cartonId,
        string comments, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.CloseOpenToteTransactions(
            company, warehouse, toteId, cartonId, comments));

    public Task<int> RelinkOpenByOrderAsync(
        string company, string warehouse, string order, string orderSuffix,
        string newToteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.RelinkOpenToteTransactions(
            company, warehouse, order, orderSuffix, newToteId));
}

/// <summary><see cref="IEmployeeLookup"/> over <see cref="InMemoryWarehouse"/>.</summary>
public sealed class FakeEmployeeLookup : IEmployeeLookup
{
    private readonly InMemoryWarehouse _warehouse;

    public FakeEmployeeLookup(InMemoryWarehouse warehouse)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
    }

    public Task<ApiEmployee?> FindByPrefixAsync(
        string company, string warehouse, string employeePrefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.FindEmployeeByPrefix(company, warehouse, employeePrefix));

    public Task<ApiEmployee?> FindByEmployeeNumberAsync(
        string employeeNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.FindEmployee(employeeNumber));
}

/// <summary><see cref="IBusinessRuleService"/> over <see cref="InMemoryWarehouse"/>.</summary>
public sealed class FakeBusinessRuleService : IBusinessRuleService
{
    private readonly InMemoryWarehouse _warehouse;
    private readonly IWmsContext _context;

    public FakeBusinessRuleService(InMemoryWarehouse warehouse, IWmsContext context)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<string?> GetRuleAsync(int parameterId, CancellationToken cancellationToken = default) =>
        GetRuleAsync(_context.Company, _context.Warehouse, parameterId, cancellationToken);

    public Task<string?> GetRuleAsync(
        string company, string warehouse, int parameterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.GetRule(company, warehouse, parameterId));

    public async Task<bool> IsEnabledAsync(
        int parameterId, string? company = null, string? warehouse = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetRuleAsync(
            company ?? _context.Company,
            warehouse ?? _context.Warehouse,
            parameterId,
            cancellationToken).ConfigureAwait(false);

        // Same loose truthiness as the real service, so behaviour does not differ.
        return value?.Trim().ToUpperInvariant() switch
        {
            "YES" or "TRUE" or "Y" or "T" or "1" => true,
            _ => false,
        };
    }

    public Task<IReadOnlyList<BusinessRuleValue>> GetRuleValuesAsync(
        int parameterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_warehouse.GetRuleValues(parameterId));
}

/// <summary>
/// <see cref="IAuditService"/> over <see cref="InMemoryWarehouse"/>.
/// </summary>
/// <remarks>
/// Keeps the real service's guard: an audit row written outside a transaction is a
/// failure, not a silent success. That guard is the whole point of the audit service, so
/// dropping it in the fake would hide exactly the bug it exists to prevent.
/// </remarks>
public sealed class FakeAuditService : IAuditService
{
    private readonly InMemoryWarehouse _warehouse;
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;

    public FakeAuditService(
        InMemoryWarehouse warehouse,
        FakeUnitOfWork unitOfWork,
        IWmsContext context)
    {
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<OperationResult<long>> AppendAsync(
        AuditTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!_unitOfWork.IsActive)
        {
            return Task.FromResult(OperationResult<long>.Failure(
                "Audit rows must be written inside the caller's unit of work.",
                "NO_ACTIVE_TRANSACTION"));
        }

        // Calls the same AuditTransaction.Enrich the real AuditService does, rather than
        // reimplementing it. The reimplementation is what broke here: it kept assigning
        // DateTimeOffset.Now after DateTime became the n_trans.t "yyyyMMddHHmm" string,
        // and it never set TransSecTime at all.
        _warehouse.AppendAudit(transaction.Enrich(
            _context.DepartmentNumber,
            _context.ShiftNumber,
            _context.OriginStack,
            DateTimeOffset.Now));

        return Task.FromResult(OperationResult<long>.Success(0));
    }

    public async Task<OperationResult> AppendManyAsync(
        IReadOnlyCollection<AuditTransaction> transactions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        foreach (var transaction in transactions)
        {
            var result = await AppendAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (result.Failed)
            {
                return result.WithoutValue();
            }
        }

        return OperationResult.Success();
    }
}
