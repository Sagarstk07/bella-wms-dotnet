using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Partners.Contracts;

/// <summary>
/// Outbound calls WMS makes into Locus Robotics.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that matters most during parallel run, because ABL calls these
/// methods today. Eight ABL files construct <c>api.wms.locusAPI</c> and call into it:
/// </para>
/// <list type="table">
///   <listheader><term>Caller</term><description>Methods used</description></listheader>
///   <item><term><c>wms_webhost/wspic_pfl.i:4011,8384</c></term><description><c>cancelOrderJob</c></description></item>
///   <item><term><c>wms_webhost/wspic_pfs.i:3525,7388</c></term><description><c>cancelOrderJob</c></description></item>
///   <item><term><c>wms_webhost/wslog_locus_priority.i:403,1430,1578</c></term><description><c>updatePriorityJob</c>, <c>createOrderJob</c></description></item>
///   <item><term><c>wms_webhost/wslog_order_inquiry.i:1833</c></term><description>order inquiry</description></item>
///   <item><term><c>remote/wsorder_mgr.p:2033</c></term><description>order drop</description></item>
///   <item><term><c>remote/wsnet_undoord.p:209</c></term><description>undo order</description></item>
///   <item><term><c>rf/wspickmove.p:246</c></term><description>pick move</description></item>
///   <item><term><c>ifaces/server/ircode.p:366</c></term><description>ERP order download</description></item>
/// </list>
/// <para>
/// <b>The dependency is mutually recursive.</b> <c>wspic_pfl.i</c> and <c>wspic_pfs.i</c>
/// are picking includes that call <c>cancelOrderJob</c>, while <c>locusAPI:pick()</c>
/// calls back into <c>wms_webhost/wspick.p</c>. Picking and the Locus adapter each depend
/// on the other across the module boundary. Phase 6 §8 lists 206 such seams needing an
/// ownership decision; this one lands in the first module converted and must be settled
/// before the outbound adapter goes live, because during parallel run ABL will be calling
/// a .NET adapter that may need to call ABL back.
/// </para>
/// </remarks>
public interface ILocusClient
{
    /// <summary>Converts <c>locusAPI.cls:362-521</c> <c>createOrderJob</c>.</summary>
    Task<OperationResult> CreateOrderJobAsync(
        string cartonId,
        string? priorityOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:523-715</c> <c>updateOrderJob</c>.</summary>
    Task<OperationResult> UpdateOrderJobAsync(
        string cartonId,
        int oldPick,
        int newPick,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:717-806</c> <c>cancelOrderJob</c>. The most-called method from ABL.</summary>
    Task<OperationResult> CancelOrderJobAsync(
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:808-899</c> <c>holdOrderJob</c> (both overloads).</summary>
    Task<OperationResult> HoldOrderJobAsync(
        string cartonId,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:901-990</c> <c>holdReleaseOrderJob</c>.</summary>
    Task<OperationResult> HoldReleaseOrderJobAsync(
        string cartonId,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:992-1293</c> <c>createPutawayJob</c>.</summary>
    Task<OperationResult> CreatePutawayJobAsync(
        string sscc18,
        string robot,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:1295-1598</c> <c>locusCreatePutawayJob</c>.</summary>
    Task<OperationResult> CreatePutawayJobForToteAsync(
        string toteId,
        string robot,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:1600-1654</c> <c>locusCreatePutawayReject</c>.</summary>
    Task<OperationResult> CreatePutawayRejectAsync(
        string toteId,
        string robot,
        CancellationToken cancellationToken = default);

    /// <summary>Converts <c>locusAPI.cls:1656-1754</c> <c>updatePriorityJob</c>.</summary>
    Task<OperationResult> UpdatePriorityJobAsync(
        string cartonId,
        string priorityNumber,
        string employeeNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells Locus a tote it tried to use is already carrying another carton. Converts
    /// <c>locusAPI.cls:953-988</c> <c>dupTote</c>, which posts an <c>OrderJob</c> with
    /// <c>EventType = "DUPTOTE"</c> and a fixed <c>JobPriority</c> of 5.
    /// </summary>
    /// <param name="singleUnit">
    /// When true the payload carries <c>"SingleUnit": "true"</c>. ⚠ <b>The string
    /// <c>"true"</c>, not a boolean</b> — <c>locusAPI.cls:969</c>, and consistent with the
    /// same quirk elsewhere in this interface. When false the property is <b>omitted
    /// entirely</b> rather than sent as false.
    /// </param>
    Task<OperationResult> SendDuplicateToteAsync(
        string cartonId,
        bool singleUnit,
        CancellationToken cancellationToken = default);
}
