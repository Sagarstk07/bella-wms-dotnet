using System.Data;
using Bella.Wms.Integration.Partners.Application;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Abstractions;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;

namespace Bella.Wms.Characterization;

/// <summary>
/// Shared in-memory test doubles for the Locus handler tests
/// (<c>LocusRejectHandlerTests</c>, <c>LocusRejectFamilyHandlerTests</c>,
/// <c>LocusPutawayRequestHandlerTests</c>). Kept minimal: only the members the handlers
/// under test actually call are implemented; everything else throws
/// <see cref="NotSupportedException"/> so an accidental new dependency shows up as a
/// failing test rather than a silent no-op.
/// </summary>
internal sealed class FakeCartonRepository : ICartonRepository
{
    private readonly Dictionary<string, Carton> _cartons = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Company, string Warehouse, string CartonId)> MarkErrorCalls { get; } = [];

    /// <summary>
    /// What <see cref="IsSingleUnitOrderAsync"/> returns. The real query counts cartons and
    /// their detail lines; the tote tests only care which branch of the duplicate-tote
    /// guard is taken, so this is a switch rather than a simulation.
    /// </summary>
    public bool SingleUnitOrder { get; set; }

    public List<(string Order, string Suffix)> SingleUnitChecks { get; } = [];

    public Carton this[string cartonId]
    {
        set => _cartons[cartonId] = value;
    }

    public Task<Carton?> FindByCartonIdAsync(
        string company, string warehouse, string cartonId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_cartons.GetValueOrDefault(cartonId));

    public Task UpdateExternalStatusAsync(
        string company, string warehouse, string cartonId, string externalStatus,
        string externalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the reject-family handler tests.");

    public Task<bool> IsSingleUnitOrderAsync(
        string company, string warehouse, string order, string orderSuffix,
        CancellationToken cancellationToken = default)
    {
        SingleUnitChecks.Add((order, orderSuffix));
        return Task.FromResult(SingleUnitOrder);
    }

    /// <summary>
    /// The item on a carton's sole detail line. Null by default — which is the ABL's
    /// behaviour for a carton with no lines <i>or</i> several, so it is the right default
    /// for a test that does not care.
    /// </summary>
    public string? SoleDetailItem { get; set; }

    public List<int> SoleDetailLookups { get; } = [];

    public Task<string?> FindSoleDetailItemAsync(
        int cartonNumber, CancellationToken cancellationToken = default)
    {
        SoleDetailLookups.Add(cartonNumber);
        return Task.FromResult(SoleDetailItem);
    }

    public Task MarkErrorUnlessCompleteAsync(
        string company, string warehouse, string cartonId,
        CancellationToken cancellationToken = default)
    {
        MarkErrorCalls.Add((company, warehouse, cartonId));
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuditService : IAuditService
{
    public List<AuditTransaction> Appended { get; } = [];

    public Task<OperationResult<long>> AppendAsync(
        AuditTransaction transaction, CancellationToken cancellationToken = default)
    {
        Appended.Add(transaction);
        return Task.FromResult(OperationResult<long>.Success(Appended.Count));
    }

    public Task<OperationResult> AppendManyAsync(
        IReadOnlyCollection<AuditTransaction> transactions,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the reject-family handler tests.");
}

internal sealed class FakeLocusClient : ILocusClient
{
    public List<(string CartonId, string? Message)> HoldOrderJobCalls { get; } = [];

    public List<(string Sscc18, string Robot)> CreatePutawayJobCalls { get; } = [];

    public List<(string ToteId, string Robot)> CreatePutawayJobForToteCalls { get; } = [];

    public List<(string CartonId, bool SingleUnit)> DuplicateToteCalls { get; } = [];

    public Task<OperationResult> HoldOrderJobAsync(
        string cartonId, string? message = null, CancellationToken cancellationToken = default)
    {
        HoldOrderJobCalls.Add((cartonId, message));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> SendDuplicateToteAsync(
        string cartonId, bool singleUnit, CancellationToken cancellationToken = default)
    {
        DuplicateToteCalls.Add((cartonId, singleUnit));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> CreateOrderJobAsync(
        string cartonId, string? priorityOverride = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");

    public Task<OperationResult> UpdateOrderJobAsync(
        string cartonId, int oldPick, int newPick, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");

    public Task<OperationResult> CancelOrderJobAsync(
        string cartonId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");

    public Task<OperationResult> HoldReleaseOrderJobAsync(
        string cartonId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");

    public Task<OperationResult> CreatePutawayJobAsync(
        string sscc18, string robot, CancellationToken cancellationToken = default)
    {
        CreatePutawayJobCalls.Add((sscc18, robot));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> CreatePutawayJobForToteAsync(
        string toteId, string robot, CancellationToken cancellationToken = default)
    {
        CreatePutawayJobForToteCalls.Add((toteId, robot));
        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> CreatePutawayRejectAsync(
        string toteId, string robot, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");

    public Task<OperationResult> UpdatePriorityJobAsync(
        string cartonId, string priorityNumber, string employeeNumber,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by the Locus handler tests.");
}

internal sealed class FakeNotificationService : INotificationService
{
    public List<(string Subject, string Body)> SentAlerts { get; } = [];

    public Task<OperationResult> SendAlertAsync(
        string subject, string body, CancellationToken cancellationToken = default)
    {
        SentAlerts.Add((subject, body));
        return Task.FromResult(OperationResult.Success());
    }
}

internal sealed class FakeUnitOfWork : IIrmsUnitOfWork
{
    public IDbConnection Connection => throw new NotSupportedException(
        "Not exercised by the reject-family handler tests — the fake repository never touches it.");

    public IDbTransaction? Transaction => null;

    public bool IsActive { get; private set; }

    public Task BeginAsync(LockOrder lockOrder, CancellationToken cancellationToken = default)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeWmsContext : IWmsContext
{
    public string Company => "01";
    public string Warehouse => "01";
    public string UserId => "TESTUSER";
    public int DepartmentNumber => 0;
    public int ShiftNumber => 0;
    public string UserType => "API";
    public DateOnly TransactionDate => DateOnly.FromDateTime(DateTime.Today);
    public string ScreenId => "/api/wms/test";
    public string? LabelPrinterId => null;
    public string OriginStack => "dotnet";
    public string CorrelationId => "test-correlation-id";
}
