namespace Bella.Wms.Platform.Identity;

/// <summary>
/// Resolves the API user from <c>empmst</c> at the request boundary.
/// </summary>
/// <remarks>
/// <para>
/// This lived in the API host in the first draft, which was wrong — the ERP queue workers
/// need it too, and a contract the host owns cannot be reached from a module. It belongs
/// to Phase 6's B15 Security, Employee &amp; Audit boundary; this project is its Wave 1
/// stub.
/// </para>
/// <para>
/// Converts the employee resolution in <c>api/wms/wsLocusAPI.p:107-123</c> and
/// <c>api/wms/wsMiddlewareAPI.p:307-315</c>.
/// </para>
/// </remarks>
public interface IEmployeeLookup
{
    /// <summary>
    /// Finds the API user whose <c>emp_num</c> starts with <paramref name="employeePrefix"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converts <c>find first empmst where empmst.emp_num begins cGsUserId and
    /// empmst.co_num eq sCompany and empmst.wh_num eq sWarehouse no-lock</c>
    /// (<c>wsLocusAPI.p:107-111</c>).
    /// </para>
    /// <para>
    /// <b>A prefix match is a poor way to authenticate.</b> If two employees in one
    /// warehouse begin with <c>LOCUS</c>, the ABL takes whichever the index yields first,
    /// which is not deterministic across index rebuilds. That behaviour is preserved for
    /// now — changing it during a conversion would be a silent change to who owns audit
    /// rows — but the implementation orders explicitly and logs when the match is
    /// ambiguous, so the problem becomes visible instead of invisible.
    /// </para>
    /// </remarks>
    Task<ApiEmployee?> FindByPrefixAsync(
        string company,
        string warehouse,
        string employeePrefix,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an employee by exact <c>emp_num</c>.
    /// Converts <c>find empmst where empmst.emp_num eq semp no-lock</c>
    /// (<c>wsMiddlewareAPI.p:307-309</c>).
    /// </summary>
    Task<ApiEmployee?> FindByEmployeeNumberAsync(
        string employeeNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>The subset of <c>empmst</c> the request boundary needs.</summary>
/// <param name="EmployeeNumber">SCHEMA-INFERRED: <c>empmst.emp_num</c>.</param>
/// <param name="Company">SCHEMA-INFERRED: <c>empmst.co_num</c>.</param>
/// <param name="Warehouse">SCHEMA-INFERRED: <c>empmst.wh_num</c>.</param>
/// <param name="DepartmentNumber">SCHEMA-INFERRED: <c>empmst.dept_num</c>.</param>
/// <param name="ShiftNumber">SCHEMA-INFERRED: <c>empmst.shf_num</c>.</param>
/// <param name="IsActiveDesktop">SCHEMA-INFERRED: <c>empmst.active_desktop</c>.</param>
/// <param name="RowStatus">SCHEMA-INFERRED: <c>empmst.row_status</c>.</param>
/// <remarks>
/// A reference type rather than a struct so that "not found" is <see langword="null"/>
/// rather than a default value with an empty employee number — the distinction matters at
/// an authentication boundary.
/// </remarks>
public sealed record ApiEmployee(
    string EmployeeNumber,
    string Company,
    string Warehouse,
    int DepartmentNumber,
    int ShiftNumber,
    bool IsActiveDesktop,
    bool RowStatus);
