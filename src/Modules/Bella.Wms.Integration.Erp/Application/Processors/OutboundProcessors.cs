using Bella.Wms.Integration.Erp.Contracts;
using Bella.Wms.Platform.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Erp.Application.Processors;

/// <summary>
/// The five outbound event types registered in <c>WMSCommOutProcess.cls</c>.
/// </summary>
/// <remarks>
/// Kept as constants rather than an enum because they are wire values FDM4 matches on,
/// and because they must round-trip through <c>comm_out.event_type</c> unchanged.
/// </remarks>
public static class ErpEventType
{
    /// <summary>Customer-order-download acknowledgement. <c>oms_ircodt.cls</c>, 91 lines.</summary>
    public const string OrderDownloadAck = "IRCODT";

    /// <summary>Order shipment / tracking update. <c>oms_irshup.cls</c>, 441 lines.</summary>
    public const string ShipmentUpdate = "IRSHUP";

    /// <summary>Order packed-complete status update. <c>oms_irpmup.cls</c>, 217 lines.</summary>
    public const string PackedUpdate = "IRPMUP";

    /// <summary>Order in-picking status update. <c>oms_irpiup.cls</c>, 218 lines.</summary>
    public const string PickingUpdate = "IRPIUP";

    /// <summary>Order drop / undrop update. <c>oms_irorup.cls</c>, 174 lines.</summary>
    public const string OrderDropUpdate = "IRORUP";

    /// <summary>Inbound customer order download. <c>oms_ircodn.cls</c>, 1,523 lines.</summary>
    public const string OrderDownload = "IRCODN";
}

/// <summary>
/// IRCODT — customer-order-download acknowledgement.
/// Converts <c>api/wms/interface/oms_ircodt.cls</c>.
/// </summary>
/// <remarks>
/// The simplest of the five: <c>Process</c> forwards the payload to the base
/// <c>SendPayload()</c> without building anything, so there is genuinely no
/// transformation to convert. It is the right first processor to wire end to end.
/// </remarks>
public sealed class OrderDownloadAckProcessor : CommOutProcessorBase
{
    public OrderDownloadAckProcessor(IFdm4Sender sender, ILogger<OrderDownloadAckProcessor> logger)
        : base(sender, logger)
    {
    }

    public override string EventType => ErpEventType.OrderDownloadAck;

    protected override Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken) =>
        // oms_ircodt.cls:27 Process passes lcInPayload straight through.
        Task.FromResult(OperationResult<string>.Success(request.InPayload));
}

/// <summary>
/// IRORUP — order drop / undrop update.
/// Converts <c>api/wms/interface/oms_irorup.cls</c>.
/// </summary>
/// <remarks>Also a pass-through in the ABL (<c>oms_irorup.cls:29</c>).</remarks>
public sealed class OrderDropUpdateProcessor : CommOutProcessorBase
{
    public OrderDropUpdateProcessor(IFdm4Sender sender, ILogger<OrderDropUpdateProcessor> logger)
        : base(sender, logger)
    {
    }

    public override string EventType => ErpEventType.OrderDropUpdate;

    protected override Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult<string>.Success(request.InPayload));
}

/// <summary>
/// IRSHUP — order shipment and tracking update.
/// Converts <c>api/wms/interface/oms_irshup.cls</c> (441 lines).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not yet converted.</b> Unlike the pass-through processors, this one builds its
/// payload in <c>create_payload</c> (<c>oms_irshup.cls:93-400</c>) from
/// <c>transactions.trans_num</c>, reading <c>shpmst</c>, <c>shpdtl</c>, <c>cartonmst</c>
/// and <c>ordhdr</c>. It has 13 queries and 4 <c>EXCLUSIVE-LOCK</c> sites.
/// </para>
/// <para>
/// Converting it needs the <c>.df</c> schema for those four tables and a captured sample
/// of the JSON FDM4 currently receives, so the field mapping can be verified rather than
/// guessed. Both are listed in <c>docs/SCHEMA_REVIEW.md</c>.
/// </para>
/// </remarks>
public sealed class ShipmentUpdateProcessor : CommOutProcessorBase
{
    public ShipmentUpdateProcessor(IFdm4Sender sender, ILogger<ShipmentUpdateProcessor> logger)
        : base(sender, logger)
    {
    }

    public override string EventType => ErpEventType.ShipmentUpdate;

    protected override Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "Converts api/wms/interface/oms_irshup.cls create_payload (lines 93-400). " +
            "Blocked on the .df schema for shpmst, shpdtl, cartonmst and ordhdr, and on " +
            "a captured sample of the payload FDM4 currently receives.");
}

/// <summary>
/// IRPMUP — order packed-complete status update.
/// Converts <c>api/wms/interface/oms_irpmup.cls</c> (217 lines).
/// </summary>
/// <remarks>
/// Builds an IRSTUP document with status "packed". Shares its shape with
/// <see cref="PickingUpdateProcessor"/> — in the ABL the two classes are near-duplicates
/// differing only in the status value, which is worth collapsing into one processor
/// parameterised by status once both are characterised.
/// </remarks>
public sealed class PackedUpdateProcessor : CommOutProcessorBase
{
    public PackedUpdateProcessor(IFdm4Sender sender, ILogger<PackedUpdateProcessor> logger)
        : base(sender, logger)
    {
    }

    public override string EventType => ErpEventType.PackedUpdate;

    protected override Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "Converts api/wms/interface/oms_irpmup.cls. Builds the IRSTUP document with " +
            "status 'packed'. Blocked on the ordhdr .df schema and a captured payload sample.");
}

/// <summary>
/// IRPIUP — order in-picking status update.
/// Converts <c>api/wms/interface/oms_irpiup.cls</c> (218 lines).
/// </summary>
public sealed class PickingUpdateProcessor : CommOutProcessorBase
{
    public PickingUpdateProcessor(IFdm4Sender sender, ILogger<PickingUpdateProcessor> logger)
        : base(sender, logger)
    {
    }

    public override string EventType => ErpEventType.PickingUpdate;

    protected override Task<OperationResult<string>> BuildPayloadAsync(
        CommOutRequest request,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            "Converts api/wms/interface/oms_irpiup.cls. Builds the IRSTUP document with " +
            "status 'picking'. Blocked on the ordhdr .df schema and a captured payload sample.");
}
