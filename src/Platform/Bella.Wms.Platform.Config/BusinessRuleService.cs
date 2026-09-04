using Bella.Wms.Platform.Context;
using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Config;

/// <summary>
/// Dapper-backed <see cref="IBusinessRuleService"/> reading <c>syspar_value</c> live.
/// </summary>
/// <remarks>
/// <para>
/// Reads run at <c>READ UNCOMMITTED</c> per <see cref="IsolationPolicy"/>. A
/// configuration read that takes a share lock would block ABL writers on the hottest
/// configuration table in the system.
/// </para>
/// <para>
/// Caching is <see cref="RequestScopedRuleCache"/> — scoped by construction, satisfying
/// Phase 6 §9: "Do not cache it in .NET beyond a request without an invalidation path."
/// </para>
/// </remarks>
public sealed class BusinessRuleService : IBusinessRuleService
{
    // SCHEMA-INFERRED. Field names harvested from ABL:
    //   tofdm4.cls:130, apiHelpers.i:568/591/614, accutechAutoBaggerOut.cls:997-1086.
    // Verify against the irms .df dump — see docs/SCHEMA_REVIEW.md.
    //
    // NOTE ON PARAMETER BINDING: the OpenEdge ODBC driver may bind positionally with '?'
    // rather than by name. If so, every named parameter in this solution needs rewriting
    // to ordered placeholders. Settle it with one query before writing more data access
    // (docs/SCHEMA_REVIEW.md open item 4).
    private const string SelectRuleSql = """
        SELECT parameter_value
        FROM   PUB.syspar_value
        WHERE  co_num       = @company
        AND    wh_num       = @warehouse
        AND    parameter_id = @parameterId
        """;

    private const string SelectRuleValuesSql = """
        SELECT co_num          AS Company,
               wh_num          AS Warehouse,
               parameter_id    AS ParameterId,
               parameter_value AS Value
        FROM   PUB.syspar_value
        WHERE  parameter_id = @parameterId
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly IWmsContext _context;
    private readonly RequestScopedRuleCache _cache;
    private readonly ILogger<BusinessRuleService> _logger;

    public BusinessRuleService(
        IIrmsUnitOfWork unitOfWork,
        IWmsContext context,
        RequestScopedRuleCache cache,
        ILogger<BusinessRuleService> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string?> GetRuleAsync(int parameterId, CancellationToken cancellationToken = default) =>
        GetRuleAsync(_context.Company, _context.Warehouse, parameterId, cancellationToken);

    public Task<string?> GetRuleAsync(
        string company,
        string warehouse,
        int parameterId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{company}:{warehouse}:{parameterId}";

        return _cache.GetOrAddAsync(key, async () =>
        {
            var value = await _unitOfWork.Connection.QueryFirstOrDefaultAsync<string?>(
                new CommandDefinition(
                    SelectRuleSql,
                    new { company, warehouse, parameterId },
                    transaction: _unitOfWork.Transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (value is null)
            {
                _logger.LogDebug(
                    "Business rule {ParameterId} is not set for {Company}/{Warehouse}.",
                    parameterId, company, warehouse);
            }

            return value;
        });
    }

    public async Task<bool> IsEnabledAsync(
        int parameterId,
        string? company = null,
        string? warehouse = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetRuleAsync(
            company ?? _context.Company,
            warehouse ?? _context.Warehouse,
            parameterId,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // ABL truthiness for rule values is loose — "yes", "true", "1" and "Y" all appear
        // in the codebase. Accepting the same set avoids a behavioural difference that
        // would only surface on one warehouse's configuration.
        return value.Trim().ToUpperInvariant() switch
        {
            "YES" or "TRUE" or "Y" or "T" or "1" => true,
            _ => false,
        };
    }

    public async Task<IReadOnlyList<BusinessRuleValue>> GetRuleValuesAsync(
        int parameterId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _unitOfWork.Connection.QueryAsync<BusinessRuleValue>(
            new CommandDefinition(
                SelectRuleValuesSql,
                new { parameterId },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }
}
