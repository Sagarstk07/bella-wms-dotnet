namespace Bella.Wms.Platform.Config;

/// <summary>
/// Reads <c>syspar_value</c>. The .NET equivalent of
/// <c>wms_webhost.static_connect:getRule(n)</c>, used 15 times in <c>api/wms</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared live with ABL (Phase 6 §9).</b> Both stacks read the same rows from the same
/// table for the whole of the parallel run. Caching is per-request only.
/// </para>
/// <para>
/// <b>Scoping.</b> <c>syspar_value</c> is keyed by company and warehouse
/// (<c>syspar_value.co_num</c>, <c>syspar_value.wh_num</c>, <c>syspar_value.parameter_id</c>,
/// <c>syspar_value.parameter_value</c>). The ABL resolves company and warehouse from the
/// ambient <c>static_connect</c>; this service resolves them from <see cref="Context.IWmsContext"/>,
/// so a caller cannot accidentally read another warehouse's configuration.
/// </para>
/// </remarks>
public interface IBusinessRuleService
{
    /// <summary>
    /// Reads a rule value for the ambient company and warehouse.
    /// Returns <see langword="null"/> when the rule is not set.
    /// </summary>
    Task<string?> GetRuleAsync(int parameterId, CancellationToken cancellationToken = default);

    /// <summary>Reads a rule for an explicit company/warehouse, for background work with no ambient context.</summary>
    Task<string?> GetRuleAsync(
        string company,
        string warehouse,
        int parameterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a rule as a boolean flag. Used for per-warehouse enables such as
    /// <see cref="BusinessRule.InterfaceApiEnabled"/> (rule 7510).
    /// </summary>
    Task<bool> IsEnabledAsync(
        int parameterId,
        string? company = null,
        string? warehouse = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every configured value for a rule across warehouses. Needed by the
    /// custOwner mapping rules (5020-5026), which the AutoBagger walks as a set.
    /// </summary>
    Task<IReadOnlyList<BusinessRuleValue>> GetRuleValuesAsync(
        int parameterId,
        CancellationToken cancellationToken = default);
}

/// <summary>One <c>syspar_value</c> row.</summary>
/// <param name="Company">SCHEMA-INFERRED: <c>syspar_value.co_num</c>.</param>
/// <param name="Warehouse">SCHEMA-INFERRED: <c>syspar_value.wh_num</c>.</param>
/// <param name="ParameterId">SCHEMA-INFERRED: <c>syspar_value.parameter_id</c>.</param>
/// <param name="Value">SCHEMA-INFERRED: <c>syspar_value.parameter_value</c>.</param>
public readonly record struct BusinessRuleValue(
    string Company,
    string Warehouse,
    int ParameterId,
    string Value);
