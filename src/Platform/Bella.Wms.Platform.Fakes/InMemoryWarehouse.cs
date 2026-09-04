using System.Collections.Concurrent;
using Bella.Wms.Integration.Partners.Domain;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Identity;

namespace Bella.Wms.Platform.Fakes;

/// <summary>
/// A small in-memory stand-in for the warehouse database, so the API can run before the
/// schema lands.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists to prove the pipeline, not the data.</b> It lets a real Locus payload
/// travel through authentication, context establishment, routing, the handler registry
/// and the audit writer, and produce a response — with nothing stubbed in between. What
/// it does not do is validate any SQL, column name or type, because none of that runs.
/// </para>
/// <para>
/// The seeded values are deliberately obvious (<c>C0001234</c>, <c>ALO</c>, <c>AV</c>)
/// so nobody mistakes a fake result for a real one.
/// </para>
/// <para>
/// <b>Delete this project once the schema arrives and integration tests run against a
/// real database.</b> A fake that outlives its purpose starts getting features, and then
/// people write tests against the fake's behaviour instead of the database's.
/// </para>
/// </remarks>
public sealed class InMemoryWarehouse
{
    private readonly ConcurrentDictionary<string, Carton> _cartons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ApiEmployee> _employees = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<AuditTransaction> _audit = [];
    private readonly ConcurrentDictionary<int, Pick> _picks = new();
    private readonly ConcurrentDictionary<int, ToteRow> _toteRows = new();
    private int _nextToteRowId;

    /// <summary>Creates a warehouse seeded with one company, one warehouse and one carton.</summary>
    public static InMemoryWarehouse CreateSeeded()
    {
        var warehouse = new InMemoryWarehouse();

        warehouse.AddCarton(new Carton
        {
            Company = "ALO",
            Warehouse = "AV",
            CartonId = "C0001234",
            CartonNumber = 1,
            Order = "SO12345",
            OrderSuffix = "01",
            BinNumber = "A-01-02-03",
            RowStatus = "A",
        });

        // Two open picks on that carton, so the tote handlers have something to work on.
        warehouse.AddPick(new Pick
        {
            Company = "ALO", Warehouse = "AV", Id = 1,
            CartonId = "C0001234", PickStatus = "O", AllowPick = true,
            Order = "SO12345", OrderSuffix = "01", Line = 1, LineSequence = 0,
            ItemNumber = "ITEM-001", BinNumber = "A-01-02-03", Quantity = 2m,
        });
        warehouse.AddPick(new Pick
        {
            Company = "ALO", Warehouse = "AV", Id = 2,
            CartonId = "C0001234", PickStatus = "O", AllowPick = true,
            Order = "SO12345", OrderSuffix = "01", Line = 2, LineSequence = 0,
            ItemNumber = "ITEM-002", BinNumber = "A-01-02-04", Quantity = 1m,
        });

        warehouse.SetSingleUnitOrder("ALO", "AV", "SO12345", "01", singleUnit: true);
        warehouse.AddCartonDetail(1, "ITEM-001");

        // An open JT row, so toteinductcancel has something to close.
        warehouse.AddToteTransaction("ALO", "AV", "TOTE-001", "C0001234");

        // The API users the ABL routers look for by prefix: "LOCUS", "PYRAMID", "aws".
        warehouse.AddEmployee(new ApiEmployee("LOCUS01", "ALO", "AV", 1, 1, true, true));
        warehouse.AddEmployee(new ApiEmployee("PYRAMID01", "ALO", "AV", 1, 1, true, true));
        warehouse.AddEmployee(new ApiEmployee("AWSAV", "ALO", "AV", 1, 1, true, true));

        // Business rule 7510 enables API-mode ERP interfacing for this warehouse.
        warehouse.SetRule("ALO", "AV", BusinessRule.InterfaceApiEnabled, "yes");
        warehouse.SetRule("ALO", "AV", BusinessRule.UnixBaseDirectory, "/tmp/bella");

        return warehouse;
    }

    public void AddCarton(Carton carton)
    {
        ArgumentNullException.ThrowIfNull(carton);
        _cartons[Key(carton.Company, carton.Warehouse, carton.CartonId)] = carton;
    }

    public Carton? FindCarton(string company, string warehouse, string cartonId) =>
        _cartons.TryGetValue(Key(company, warehouse, cartonId), out var carton) ? carton : null;

    public void UpdateCartonExternalStatus(
        string company, string warehouse, string cartonId, string externalStatus, string externalId)
    {
        var key = Key(company, warehouse, cartonId);

        if (_cartons.TryGetValue(key, out var existing))
        {
            _cartons[key] = existing with
            {
                ExternalStatus = externalStatus,
                ExternalId = externalId,
            };
        }
    }

    /// <summary>
    /// The reject family's conditional status write: set <c>row_status</c> to <c>E</c>
    /// unless it is already <c>C</c>. Mirrors the single conditional <c>UPDATE</c> that
    /// <c>ICartonRepository.MarkErrorUnlessCompleteAsync</c> specifies, not the ABL's
    /// separate re-find-then-check-then-write.
    /// </summary>
    /// <returns>
    /// The number of rows the equivalent SQL would have affected — 1 if the status
    /// changed, 0 if the carton was already <c>C</c> or does not exist. Zero is an
    /// expected outcome, not an error.
    /// </returns>
    public int MarkCartonErrorUnlessComplete(string company, string warehouse, string cartonId)
    {
        var key = Key(company, warehouse, cartonId);

        // Case-insensitive, matching the real repository's NOT IN ('C','c') — ABL
        // character comparison ignores case and this database opts out on exactly one
        // field. A fake that is stricter than the thing it stands in for hides bugs.
        if (!_cartons.TryGetValue(key, out var existing) ||
            string.Equals(existing.RowStatus, "C", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        _cartons[key] = existing with { RowStatus = "E" };
        return 1;
    }

    public void AddPick(Pick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        _picks[pick.Id] = pick;
    }

    public IReadOnlyList<Pick> FindPicksByCarton(
        string company, string warehouse, string cartonId, bool openPicksOnly) =>
        [.. _picks.Values
            .Where(p => p.Company.Equals(company, StringComparison.OrdinalIgnoreCase)
                     && p.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(p.CartonId, cartonId, StringComparison.OrdinalIgnoreCase)
                     && (!openPicksOnly
                         || string.Equals(p.PickStatus, "O", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Id)];

    /// <summary>
    /// Writes the robot and tote ids into every matching pick — the in-memory equivalent of
    /// the single <c>UPDATE</c> in <c>PickRepository.SetToteAssignmentAsync</c>.
    /// </summary>
    public int SetToteAssignment(
        string company, string warehouse, string cartonId,
        string jobRobot, string toteId, bool openPicksOnly)
    {
        var matches = FindPicksByCarton(company, warehouse, cartonId, openPicksOnly);

        foreach (var pick in matches)
        {
            _picks[pick.Id] = pick with { ToteRobot = jobRobot, ToteId = toteId };
        }

        return matches.Count;
    }

    /// <summary>
    /// An open <c>JT</c> transaction row. The real system keeps these in the
    /// <c>transactions</c> table — see <c>IToteTransactionRepository</c> for why the audit
    /// table is not purely append-only.
    /// </summary>
    private sealed record ToteRow(string Company, string Warehouse, string ToteId,
        string? CartonId, bool IsVoid, string RowStatus);

    public void AddToteTransaction(
        string company, string warehouse, string toteId, string? cartonId,
        bool isVoid = false, string rowStatus = "O")
    {
        var id = Interlocked.Increment(ref _nextToteRowId);
        _toteRows[id] = new ToteRow(company, warehouse, toteId, cartonId, isVoid, rowStatus);
    }

    public (string? CartonId, bool IsVoid)? FindOpenToteTransaction(
        string company, string warehouse, string toteId) =>
        _toteRows.Values
            .Where(r => Match(r, company, warehouse, toteId))
            .Select(r => ((string?)r.CartonId, r.IsVoid))
            .Cast<(string?, bool)?>()
            .FirstOrDefault();

    public int CloseOpenToteTransactions(
        string company, string warehouse, string toteId, string? cartonId, string comments)
    {
        var closed = 0;

        foreach (var (id, row) in _toteRows)
        {
            if (!Match(row, company, warehouse, toteId)) continue;
            if (cartonId is not null &&
                !string.Equals(row.CartonId, cartonId, StringComparison.OrdinalIgnoreCase)) continue;

            _toteRows[id] = row with { RowStatus = "C" };
            closed++;
        }

        return closed;
    }

    private static bool Match(ToteRow r, string company, string warehouse, string toteId) =>
        r.Company.Equals(company, StringComparison.OrdinalIgnoreCase)
        && r.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
        && r.ToteId.Equals(toteId, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(r.RowStatus, "C", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Orders explicitly marked single-unit. The fake has no <c>cartondtl</c> table, so it
    /// cannot count detail lines the way the real query does — this is a switch tests flip
    /// to exercise both sides of the duplicate-tote guard.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _singleUnitOrders = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<int, List<string>> _cartonDetails = new();

    /// <summary>Adds a <c>cartondtl</c> line. Two lines make the carton ambiguous, which is what the ABL treats as "not found".</summary>
    public void AddCartonDetail(int cartonNumber, string itemNumber) =>
        _cartonDetails.AddOrUpdate(cartonNumber, _ => [itemNumber], (_, list) => { list.Add(itemNumber); return list; });

    public string? FindSoleDetailItem(int cartonNumber) =>
        _cartonDetails.TryGetValue(cartonNumber, out var lines) && lines.Count == 1 ? lines[0] : null;

    public void SetSingleUnitOrder(
        string company, string warehouse, string order, string orderSuffix, bool singleUnit) =>
        _singleUnitOrders[Key(company, warehouse, $"{order}|{orderSuffix}")] = singleUnit;

    public bool IsSingleUnitOrder(string company, string warehouse, string order, string orderSuffix) =>
        _singleUnitOrders.TryGetValue(Key(company, warehouse, $"{order}|{orderSuffix}"), out var v) && v;

    public int RelinkOpenToteTransactions(
        string company, string warehouse, string order, string orderSuffix, string newToteId)
    {
        // The fake's tote rows carry a carton, not an order, so resolve through the
        // cartons: every carton on the order, then every open row for those cartons.
        var cartonIds = _cartons.Values
            .Where(c => c.Company.Equals(company, StringComparison.OrdinalIgnoreCase)
                     && c.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(c.Order, order, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(c.OrderSuffix, orderSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.CartonId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relinked = 0;

        foreach (var (id, row) in _toteRows)
        {
            // The real query narrows to row_status = 'O' exactly, not <> 'C'.
            if (!string.Equals(row.RowStatus, "O", StringComparison.OrdinalIgnoreCase)) continue;
            if (row.CartonId is null || !cartonIds.Contains(row.CartonId)) continue;

            _toteRows[id] = row with { ToteId = newToteId };
            relinked++;
        }

        return relinked;
    }

    public void AddEmployee(ApiEmployee employee)
    {
        ArgumentNullException.ThrowIfNull(employee);
        _employees[employee.EmployeeNumber] = employee;
    }

    /// <summary>
    /// Mirrors the ABL prefix match at <c>wsLocusAPI.p:107-111</c>, including its
    /// non-determinism when more than one employee matches — ordered here so at least the
    /// fake is repeatable.
    /// </summary>
    public ApiEmployee? FindEmployeeByPrefix(string company, string warehouse, string prefix) =>
        _employees.Values
            .Where(e => e.Company.Equals(company, StringComparison.OrdinalIgnoreCase)
                     && e.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase)
                     && e.EmployeeNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.EmployeeNumber, StringComparer.Ordinal)
            .FirstOrDefault();

    public ApiEmployee? FindEmployee(string employeeNumber) =>
        _employees.TryGetValue(employeeNumber, out var employee) ? employee : null;

    public void SetRule(string company, string warehouse, int parameterId, string value) =>
        _rules[Key(company, warehouse, parameterId.ToString())] = value;

    public string? GetRule(string company, string warehouse, int parameterId) =>
        _rules.TryGetValue(Key(company, warehouse, parameterId.ToString()), out var value) ? value : null;

    public IReadOnlyList<BusinessRuleValue> GetRuleValues(int parameterId) =>
        [.. _rules
            .Where(kv => kv.Key.EndsWith($"|{parameterId}", StringComparison.Ordinal))
            .Select(kv =>
            {
                var parts = kv.Key.Split('|');
                return new BusinessRuleValue(parts[0], parts[1], parameterId, kv.Value);
            })];

    public void AppendAudit(AuditTransaction transaction) => _audit.Add(transaction);

    /// <summary>Every audit row written this session — the assertion target for tests.</summary>
    public IReadOnlyCollection<AuditTransaction> AuditTrail => [.. _audit];

    private static string Key(string company, string warehouse, string id) =>
        $"{company.ToUpperInvariant()}|{warehouse.ToUpperInvariant()}|{id.ToUpperInvariant()}";
}
