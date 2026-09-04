namespace Bella.Wms.Platform.Context;

/// <summary>
/// Mutable scoped implementation of <see cref="IWmsContext"/>, populated once at the
/// API boundary and read-only in effect thereafter.
/// </summary>
/// <remarks>
/// Registered as <c>AddScoped</c>. That scoping is what replaces
/// <c>wms_webhost/wmswipeout.p</c>, which the ABL routers must call in a
/// <c>FINALLY</c> block on every request (<c>wsLocusAPI.p:39</c>,
/// <c>wsMiddlewareAPI.p:41</c>) because <c>static_connect</c> is process-static and
/// PASOE reuses agents between requests. A scoped object cannot leak into the next
/// request, so there is nothing to wipe.
/// </remarks>
public sealed class WmsContext : IWmsContext
{
    public string Company { get; private set; } = string.Empty;

    public string Warehouse { get; private set; } = string.Empty;

    public string UserId { get; private set; } = string.Empty;

    public int DepartmentNumber { get; private set; }

    public int ShiftNumber { get; private set; }

    public string UserType { get; private set; } = string.Empty;

    public DateOnly TransactionDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    public string ScreenId { get; private set; } = string.Empty;

    public string? LabelPrinterId { get; private set; }

    public string OriginStack { get; } = "DOTNET";

    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>True once <see cref="Initialise"/> has run.</summary>
    public bool IsInitialised { get; private set; }

    /// <summary>
    /// The .NET equivalent of <c>static_connect:initConnect(company, warehouse, user, "", "")</c>
    /// (<c>wsLocusAPI.p:117</c>).
    /// </summary>
    /// <remarks>
    /// <b>Behavioural difference from ABL, deliberate.</b> When <c>wsLocusAPI.p</c>
    /// cannot find the LOCUS employee it logs a message and carries on with an
    /// <i>uninitialised</i> <c>static_connect</c> (lines 113-125), so the request
    /// proceeds with whatever company/warehouse the previous request left behind.
    /// Here, an uninitialised context is a hard failure at the boundary. Recorded in
    /// <c>docs/ABL_CROSSREF.md</c> as a deliberate difference and covered by a
    /// characterization test.
    /// </remarks>
    public void Initialise(
        string company,
        string warehouse,
        string userId,
        string userType = "",
        string? labelPrinterId = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(warehouse);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Company = company.Trim().ToUpperInvariant();
        Warehouse = warehouse.Trim().ToUpperInvariant();
        UserId = userId.Trim().ToUpperInvariant();
        UserType = userType ?? string.Empty;
        LabelPrinterId = labelPrinterId;

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationId = correlationId;
        }

        IsInitialised = true;
    }

    /// <summary>
    /// Applies the employee attributes the ABL routers copy out of <c>empmst</c>
    /// (<c>wsLocusAPI.p:119-122</c>, <c>wsMiddlewareAPI.p:311-315</c>).
    /// </summary>
    public void ApplyEmployee(int departmentNumber, int shiftNumber)
    {
        DepartmentNumber = departmentNumber;
        ShiftNumber = shiftNumber;
    }

    /// <summary>Sets <c>screenid</c> and <c>transdate</c> as <c>wsMiddlewareAPI.p:304-305</c> does.</summary>
    public void ApplyOrigin(string screenId, DateOnly? transactionDate = null)
    {
        ScreenId = screenId ?? string.Empty;
        TransactionDate = transactionDate ?? DateOnly.FromDateTime(DateTime.Today);
    }
}
