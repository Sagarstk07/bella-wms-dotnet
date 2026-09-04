using Bella.Wms.Platform.Abstractions;

namespace Bella.Wms.Integration.Erp.Contracts;

/// <summary>
/// Contract every outbound (WMS → OMS) processor implements.
/// </summary>
/// <remarks>
/// <para>
/// Direct translation of <c>api/wms/interface/IWMSCommOutProcessor.cls</c>. The ABL
/// signature is:
/// </para>
/// <code>
/// METHOD PUBLIC LOGICAL Process(
///    input  lcInPayload     as longchar,
///    input  iRefTransNum    as int,      // transactions.trans_num
///    input  cTransmissionId as char,     // part of each outbound payload
///    output lcOutPayload    as longchar, // payload sent to the host system
///    output lcResponse      as longchar,
///    output iStatusCode     as int,
///    output cMessage        as char).
/// </code>
/// <para>
/// The four <c>output</c> parameters plus the <c>logical</c> return collapse into
/// <see cref="CommOutResult"/> and <see cref="OperationResult{T}"/>. This is the one
/// place in <c>api/</c> where the ABL already had a proper interface, so the conversion
/// is a rename rather than a redesign.
/// </para>
/// </remarks>
public interface ICommOutProcessor
{
    /// <summary>
    /// The <c>event_type</c> this processor handles. Replaces the <c>CASE iFaceType</c>
    /// branch in <c>WMSCommOutProcess.cls</c> — registration is now a property of the
    /// processor rather than a switch someone must remember to extend.
    /// </summary>
    string EventType { get; }

    /// <summary>The <c>comm_endpoint</c> this processor serves. <c>"OMS"</c> today.</summary>
    string Endpoint { get; }

    Task<OperationResult<CommOutResult>> ProcessAsync(
        CommOutRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input to <see cref="ICommOutProcessor.ProcessAsync"/>.</summary>
/// <param name="InPayload">ABL <c>lcInPayload</c>.</param>
/// <param name="ReferenceTransactionNumber">ABL <c>iRefTransNum</c> — <c>transactions.trans_num</c>.</param>
/// <param name="TransmissionId">ABL <c>cTransmissionId</c>.</param>
public readonly record struct CommOutRequest(
    string InPayload,
    long ReferenceTransactionNumber,
    string TransmissionId);

/// <summary>Output of <see cref="ICommOutProcessor.ProcessAsync"/>.</summary>
/// <param name="OutPayload">ABL <c>lcOutPayload</c> — what was sent to the host system.</param>
/// <param name="Response">ABL <c>lcResponse</c> — what the host system returned.</param>
/// <param name="StatusCode">ABL <c>iStatusCode</c>.</param>
public readonly record struct CommOutResult(
    string OutPayload,
    string Response,
    int StatusCode);
