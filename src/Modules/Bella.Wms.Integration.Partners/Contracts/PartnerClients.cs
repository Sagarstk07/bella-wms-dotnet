using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Partners.Contracts;

/// <summary>
/// Outbound calls WMS makes into Pyramid WMS.
/// </summary>
/// <remarks>
/// Converts the outbound methods of <c>api/wms/pyramidAPI.cls</c> (673 lines).
/// Called from <c>wms_webhost/wsmat_pick_module.i:421</c> and
/// <c>wms_webhost/wsshp_dock2.i:505</c>.
/// Pyramid handles orders requiring service-tech treatment; its config is derived from
/// the Locus config path by replacing "locus" with "pyramid".
/// </remarks>
public interface IPyramidClient
{
    /// <summary>Converts <c>pyramidAPI.cls:166-290</c> <c>createOLPNUPDATE</c>.</summary>
    Task<OperationResult> CreateOlpnUpdateAsync(
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>pyramidAPI.cls:292-334</c> <c>createReplen</c>.</summary>
    Task<OperationResult> CreateReplenishmentAsync(
        string toteId,
        string zone,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>pyramidAPI.cls:336-501</c> <c>createPick</c>.</summary>
    Task<OperationResult> CreatePickAsync(
        string parentToteId,
        string toteId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbound calls WMS makes into UPS.
/// </summary>
/// <remarks>
/// <para>
/// Converts <c>api/wms/upsAPI.cls</c> (799 lines). Called from
/// <c>wms_webhost/wsship.p:2895</c>.
/// </para>
/// <para>
/// <b>Two things make this one different from the other adapters.</b> It is the only
/// partner using OAuth2 rather than a static key, and it is the only one that reaches a
/// remote AppServer: <c>upsAPI.cls:424</c> runs <c>ms/dhl.p</c> on <c>hAppSrv2</c> to
/// fetch <c>getOrderInfo</c> and <c>getItemData</c>. That AppServer hop has to be
/// replaced by a direct query or a call into the owning module, and which one depends on
/// where <c>ms/dhl.p</c> ends up. It is not in the <c>api/</c> tree.
/// </para>
/// </remarks>
public interface IUpsClient
{
    /// <summary>
    /// Converts <c>upsAPI.cls:72-175</c> <c>getAuthToken</c>.
    /// </summary>
    /// <remarks>
    /// The ABL fetches a token per operation. Token caching with expiry-aware refresh is
    /// the obvious improvement, and is a behavioural difference worth recording because
    /// it changes the request pattern UPS sees.
    /// </remarks>
    Task<OperationResult<string>> GetAuthTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Converts <c>upsAPI.cls:177-793</c> <c>generateUPSShippingLabel</c>.</summary>
    Task<OperationResult<string>> GenerateShippingLabelAsync(
        string cartonId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbound calls WMS makes into the AWS EventBridge middleware for the Accutech
/// AutoBagger.
/// </summary>
/// <remarks>
/// <para>
/// Converts the outbound half of <c>api/wms/accutechAutoBaggerOut.cls</c> (1,977 lines)
/// and <c>api/wms/middleware.cls</c> (473 lines). Called from
/// <c>wms_webhost/send_to_external_system.i:87</c>.
/// </para>
/// <para>
/// <b>Mostly deferred.</b> <c>accutechAutoBaggerOut.cls:776</c> runs
/// <c>wms_webhost/wsship.p</c> persistently for <c>ShipCarton</c> and
/// <c>getShipCartonLablePath</c>, so the shipping-side methods carry the same blocker as
/// the Locus pick path. The eligibility and grouping queries do not, and can convert
/// independently.
/// </para>
/// </remarks>
public interface IAutoBaggerClient
{
    /// <summary>
    /// Converts <c>accutechAutoBaggerOut.cls:570-591</c> <c>getAutoBaggerEligible</c>.
    /// Read-only; no warehouse-core dependency.
    /// </summary>
    Task<OperationResult<string>> GetEligibilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <c>accutechAutoBaggerOut.cls:593-682</c> <c>getLocusGrouping</c>.
    /// Read-only; no warehouse-core dependency.
    /// </summary>
    Task<OperationResult<string>> GetLocusGroupingAsync(
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <c>accutechAutoBaggerOut.cls:501-568</c> <c>isValidPackage</c>.
    /// Gated on business rule 3490.
    /// </summary>
    Task<OperationResult<bool>> IsValidPackageAsync(
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <c>accutechAutoBaggerOut.cls:200-499</c> <c>cartonPacked</c>.
    /// <b>Deferred</b> — reaches <c>wms_webhost/wsship.p</c>.
    /// </summary>
    Task<OperationResult> NotifyCartonPackedAsync(
        string cartonId,
        CancellationToken cancellationToken = default);
}
