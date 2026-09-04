namespace Bella.Wms.Platform.Config;

/// <summary>
/// The business rule identifiers <c>api/wms</c> reads from <c>syspar_value</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every value here was found by reading the ABL; the comment on each names the file and
/// line. Rules read through <c>static_connect:getRule(n)</c> and rules queried directly
/// as <c>syspar_value.parameter_id eq n</c> are the same table, so they are one list.
/// </para>
/// <para>
/// <b>Phase 6 §9 obligation.</b> <c>syspar_value</c> drives behaviour across every
/// boundary and must be read live from the same table by both stacks. Do not cache
/// beyond a request without an invalidation path, and never fork these into a .NET-side
/// config file — a divergence produces two systems that disagree about the rules while
/// both believe they are right.
/// </para>
/// </remarks>
public static class BusinessRule
{
    /// <summary>cURL certificate path. <c>apiHelpers.i:568</c>, referenced <c>middleware.cls:426</c>.</summary>
    /// <remarks>Obsolete after conversion: TLS trust is the .NET certificate store's job.</remarks>
    public const int CurlCertificatePath = 59;

    /// <summary>cURL binary path. <c>apiHelpers.i:591</c>. Obsolete after conversion.</summary>
    public const int CurlBinaryPath = 60;

    /// <summary>cURL library path. <c>apiHelpers.i:614</c>. Obsolete after conversion.</summary>
    public const int CurlLibraryPath = 61;

    /// <summary>UPS API configuration. Phase 2 records this as the UPS config rule.</summary>
    public const int UpsConfiguration = 1213;

    /// <summary>Unix base directory. <c>wsMiddlewareAPI.p:321</c>, <c>rest/inbound.p:196</c>.</summary>
    public const int UnixBaseDirectory = 2013;

    /// <summary>Package validation rule used by AutoBagger. <c>accutechAutoBaggerOut.cls:997</c>.</summary>
    public const int PackageValidation = 3490;

    /// <summary>Locus / Pyramid config file path. <c>locusResultTest.cls:80</c>.</summary>
    public const int LocusConfigurationPath = 5018;

    /// <summary>Locus customer-owner mapping: ALOB2C. <c>accutechAutoBaggerOut.cls:1035</c>.</summary>
    public const int LocusCustOwnerAloB2C = 5020;

    /// <summary>Locus customer-owner mapping: ALOSPECIAL. <c>accutechAutoBaggerOut.cls:1020,1042</c>.</summary>
    public const int LocusCustOwnerAloSpecial = 5021;

    /// <summary>Locus customer-owner mapping: ALODTS. <c>accutechAutoBaggerOut.cls:1049</c>.</summary>
    public const int LocusCustOwnerAloDts = 5022;

    /// <summary>Locus customer-owner mapping: ALOB2B. <c>accutechAutoBaggerOut.cls:1056</c>.</summary>
    public const int LocusCustOwnerAloB2B = 5023;

    /// <summary>Locus customer-owner mapping: BEL. <c>accutechAutoBaggerOut.cls:1063</c>.</summary>
    public const int LocusCustOwnerBel = 5026;

    /// <summary>Order codes routed to SALT. <c>accutechAutoBaggerOut.cls:548,1086</c>.</summary>
    public const int SaltOrderCodes = 5027;

    /// <summary>AutoBagger config file path. <c>accutechAutoBagger.cls:153</c>.</summary>
    public const int AutoBaggerConfigurationPath = 6021;

    /// <summary>Middleware config file path. <c>middleware.cls:106</c>.</summary>
    public const int MiddlewareConfigurationPath = 6022;

    /// <summary>
    /// Interface API base path. <c>cron_connect.i:51</c>, <c>oms_comm_purge.p:39</c>,
    /// <c>WMSCommOutProcessorBase.cls:119</c>.
    /// </summary>
    public const int InterfaceApiPath = 7000;

    /// <summary>
    /// Per-warehouse enable for API-mode ERP interfacing. <c>tofdm4.cls:130</c>.
    /// When off, the warehouse uses the file-based <c>ifaces/</c> path instead.
    /// </summary>
    public const int InterfaceApiEnabled = 7510;
}
