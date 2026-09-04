using Bella.Wms.Platform.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Platform.Identity;

/// <summary>
/// Dapper implementation of <see cref="IEmployeeLookup"/> against <c>empmst</c> in
/// <c>irms</c>.
/// </summary>
/// <remarks>
/// <b>SCHEMA-INFERRED.</b> Column names harvested from the ABL; the create block in
/// <c>wsMiddlewareAPI.p:439-453</c> is the most complete single list of this table's
/// fields in the module. Verify against the <c>irms</c> <c>.df</c>.
/// </remarks>
public sealed class EmployeeRepository : IEmployeeLookup
{
    private const string SelectColumns = """
        emp_num        AS EmployeeNumber,
        co_num         AS Company,
        wh_num         AS Warehouse,
        dept_num       AS DepartmentNumber,
        shf_num        AS ShiftNumber,
        active_desktop AS IsActiveDesktop,
        row_status     AS RowStatus
        """;

    private readonly IIrmsUnitOfWork _unitOfWork;
    private readonly ILogger<EmployeeRepository> _logger;

    public EmployeeRepository(IIrmsUnitOfWork unitOfWork, ILogger<EmployeeRepository> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ApiEmployee?> FindByPrefixAsync(
        string company,
        string warehouse,
        string employeePrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeePrefix);

        // ABL `begins` is a prefix match. LIKE with an escaped pattern is the SQL
        // equivalent; the prefix comes from a fixed internal constant ("LOCUS",
        // "PYRAMID", "aws"), never from request data, so there is no injection path —
        // but it is escaped anyway rather than relying on that staying true.
        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.empmst
            WHERE  co_num  = @company
            AND    wh_num  = @warehouse
            AND    emp_num LIKE @prefix ESCAPE '\'
            ORDER BY emp_num
            """;

        var matches = await _unitOfWork.Connection.QueryAsync<ApiEmployee>(
            new CommandDefinition(
                sql,
                new
                {
                    company,
                    warehouse,
                    prefix = EscapeLikePrefix(employeePrefix) + "%",
                },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var candidates = matches.ToArray();

        if (candidates.Length == 0)
        {
            // ABL wsLocusAPI.p:124-125 logs and then continues with an uninitialised
            // static_connect. The caller here treats null as a hard failure instead —
            // see WmsContextMiddleware.
            _logger.LogWarning(
                "No employee beginning '{Prefix}' for {Company}/{Warehouse}.",
                employeePrefix, company, warehouse);
            return null;
        }

        if (candidates.Length > 1)
        {
            // The ABL cannot see this happening. Making it visible is the point: two
            // API users in one warehouse means audit rows are being attributed
            // non-deterministically.
            _logger.LogError(
                "{Count} employees begin '{Prefix}' for {Company}/{Warehouse} ({Matches}). " +
                "Using '{Selected}'. This is ambiguous and should be corrected in empmst.",
                candidates.Length,
                employeePrefix,
                company,
                warehouse,
                string.Join(", ", candidates.Select(c => c.EmployeeNumber)),
                candidates[0].EmployeeNumber);
        }

        return candidates[0];
    }

    public async Task<ApiEmployee?> FindByEmployeeNumberAsync(
        string employeeNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNumber);

        var sql = $"""
            SELECT {SelectColumns}
            FROM   PUB.empmst
            WHERE  emp_num = @employeeNumber
            """;

        return await _unitOfWork.Connection.QueryFirstOrDefaultAsync<ApiEmployee>(
            new CommandDefinition(
                sql,
                new { employeeNumber },
                transaction: _unitOfWork.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Escapes LIKE metacharacters so a prefix is matched literally.</summary>
    private static string EscapeLikePrefix(string prefix) =>
        prefix.Replace("\\", "\\\\", StringComparison.Ordinal)
              .Replace("%", "\\%", StringComparison.Ordinal)
              .Replace("_", "\\_", StringComparison.Ordinal);
}
