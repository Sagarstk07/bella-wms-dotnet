using System.Text.Json.Serialization;

namespace Bella.Wms.Integration.Partners.Contracts.Locus;

/// <summary>
/// The envelope Locus posts to us. Exactly one of the three properties is present, and
/// which one selects the routing branch.
/// </summary>
/// <remarks>
/// <para>
/// Recovered from <c>api/wms/wsLocusAPI.p:131-300</c>, which tests each wrapper in turn
/// with <c>jsoRequest:has(...)</c>.
/// </para>
/// <para>
/// <b>These contracts are read out of the ABL, not from captured traffic.</b> They are
/// the best available specification until the Phase 5 harness runs, and they should be
/// validated against a week of real payloads before anyone relies on them. Where the ABL
/// and the comment blocks disagree, the code wins and the disagreement is noted.
/// </para>
/// </remarks>
public sealed record LocusInboundEnvelope
{
    /// <summary>Order job lifecycle and picking. <c>wsLocusAPI.p:131</c>.</summary>
    [JsonPropertyName("OrderJobResult")]
    public LocusOrderJobResult? OrderJobResult { get; init; }

    /// <summary>Locus asking us to create a putaway job. <c>wsLocusAPI.p:195</c>.</summary>
    [JsonPropertyName("PutawayJobRequest")]
    public LocusPutawayJobRequest? PutawayJobRequest { get; init; }

    /// <summary>Putaway and replenishment results. <c>wsLocusAPI.p:237</c>.</summary>
    [JsonPropertyName("PutawayJobResult")]
    public LocusPutawayJobResult? PutawayJobResult { get; init; }
}

/// <summary>
/// Result of an order job — the payload behind 15 of the 22 inbound events.
/// </summary>
/// <remarks>
/// Field list from the parse sites in <c>locusAPI.cls</c>, principally
/// <c>accept</c> (1756), <c>reject</c> (1803) and <c>pick</c> (2544-2640).
/// </remarks>
public sealed record LocusOrderJobResult
{
    /// <summary>
    /// Selects the handler. <c>wsLocusAPI.p:138</c> reads this and dispatches on it.
    /// Values are in <see cref="Domain.LocusEventType"/>.
    /// </summary>
    [JsonPropertyName("EventType")]
    public string? EventType { get; init; }

    /// <summary>The carton id. <c>locusAPI.cls:1761</c> — matched against <c>cartonmst.carton_id</c>.</summary>
    [JsonPropertyName("JobId")]
    public string? JobId { get; init; }

    /// <summary><c>locusAPI.cls:1762</c>.</summary>
    [JsonPropertyName("JobStatus")]
    public string? JobStatus { get; init; }

    /// <summary>
    /// ISO date. <c>locusAPI.cls:1763</c>.
    /// Deliberately <see cref="string"/>: the ABL never parses it (line 1793 is commented
    /// out with "need to format to format in n_trans.t"), so nothing yet establishes the
    /// format Locus actually sends. Parse it once captured traffic confirms the shape.
    /// </summary>
    [JsonPropertyName("JobDate")]
    public string? JobDate { get; init; }

    /// <summary>Robot identifier.</summary>
    [JsonPropertyName("JobRobot")]
    public string? JobRobot { get; init; }

    /// <summary>Station identifier.</summary>
    [JsonPropertyName("JobStation")]
    public string? JobStation { get; init; }

    /// <summary>Pick method reported by Locus.</summary>
    [JsonPropertyName("JobMethod")]
    public string? JobMethod { get; init; }

    /// <summary>Free-text detail on reject and hold events.</summary>
    [JsonPropertyName("EventInfo")]
    public string? EventInfo { get; init; }

    /// <summary>Tote identifier. Used by the tote induct and move events.</summary>
    [JsonPropertyName("ToteId")]
    public string? ToteId { get; init; }

    /// <summary>Customer owner / drop-off location.</summary>
    [JsonPropertyName("CustOwner")]
    public string? CustOwner { get; init; }

    /// <summary>Exception code on failure events.</summary>
    [JsonPropertyName("ExceptionCode")]
    public string? ExceptionCode { get; init; }

    /// <summary>Exception detail on failure events.</summary>
    [JsonPropertyName("ExceptionReason")]
    public string? ExceptionReason { get; init; }

    /// <summary>Message text.</summary>
    [JsonPropertyName("Message")]
    public string? Message { get; init; }

    /// <summary>The task detail. See <see cref="LocusOrderJobTasks"/> for the shape hazard.</summary>
    [JsonPropertyName("JobTasks")]
    public LocusOrderJobTasks? JobTasks { get; init; }
}

/// <summary>
/// Task wrapper on an order job result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape hazard, carried deliberately.</b> The ABL reads this as a <i>single object</i>:
/// </para>
/// <code>
/// jsoOrderJobResultTasks = jsoJobTasks:getjsonobject("OrderJobResultTask").  // line 2621
/// </code>
/// <para>
/// while the putaway equivalent reads an <i>array</i>:
/// </para>
/// <code>
/// jsaPutawayJobResultTask = jsoJobTasks:getjsonarray("PutawayJobResultTask"). // line 3271
/// </code>
/// <para>
/// So a multi-task pick result either cannot occur, or is being silently truncated to its
/// first task today — and the ABL would not report the difference, because the call is
/// wrapped in <c>NO-ERROR</c>. This is modelled as a single object to match current
/// behaviour, and it is a specific question for the captured traffic: <b>does Locus ever
/// send more than one OrderJobResultTask?</b> If it does, we have found a live defect.
/// </para>
/// </remarks>
public sealed record LocusOrderJobTasks
{
    [JsonPropertyName("OrderJobResultTask")]
    public LocusOrderJobResultTask? OrderJobResultTask { get; init; }
}

/// <summary>One task within an order job result. Fields from <c>locusAPI.cls:2626-2639</c>.</summary>
public sealed record LocusOrderJobResultTask
{
    /// <summary>Maps to <c>pick.id</c> on the way out; echoed back here.</summary>
    [JsonPropertyName("JobTaskId")]
    public string? JobTaskId { get; init; }

    /// <summary>Order number and suffix joined with a hyphen, as sent in <c>createOrderJob</c>.</summary>
    [JsonPropertyName("OrderId")]
    public string? OrderId { get; init; }

    [JsonPropertyName("CustOwner")]
    public string? CustOwner { get; init; }

    [JsonPropertyName("TaskStatus")]
    public string? TaskStatus { get; init; }

    [JsonPropertyName("TaskType")]
    public string? TaskType { get; init; }

    /// <summary>Bin number.</summary>
    [JsonPropertyName("TaskLocation")]
    public string? TaskLocation { get; init; }

    /// <summary>
    /// Quantity requested. <see cref="string"/> because the ABL parses it as text and
    /// converts with <c>decimal(...)</c> at the point of use — a non-numeric value
    /// currently fails there rather than at parse time, and changing that changes
    /// behaviour.
    /// </summary>
    [JsonPropertyName("TaskQty")]
    public string? TaskQty { get; init; }

    /// <summary>Quantity actually picked. Same string treatment as <see cref="TaskQty"/>.</summary>
    [JsonPropertyName("ExecQty")]
    public string? ExecQty { get; init; }

    /// <summary>
    /// When the pick happened. Fed to <c>queueDelay()</c> (<c>locusAPI.cls:4878</c>) which
    /// writes the result into <c>transactions.ns_comment</c>.
    /// </summary>
    [JsonPropertyName("ExecDate")]
    public string? ExecDate { get; init; }

    /// <summary>
    /// The Locus operator. Written to <c>transactions.emp_num</c> and later copied into
    /// <c>transactions.comment</c> by the re-find at <c>locusAPI.cls:3013-3025</c>.
    /// </summary>
    [JsonPropertyName("ExecUser")]
    public string? ExecUser { get; init; }

    /// <summary>Item number — <c>pick.abs_num</c>.</summary>
    [JsonPropertyName("ItemNo")]
    public string? ItemNo { get; init; }

    [JsonPropertyName("ExceptionCode")]
    public string? ExceptionCode { get; init; }

    [JsonPropertyName("ExceptionReason")]
    public string? ExceptionReason { get; init; }

    /// <summary>
    /// Captured serial number, requested via <c>CaptureSerialNo</c> on the outbound task.
    /// Read by <c>getCOOfromSerialNo</c> (<c>locusAPI.cls:4975</c>) to derive
    /// country-of-origin.
    /// </summary>
    [JsonPropertyName("SerialNo")]
    public string? SerialNo { get; init; }
}

/// <summary>
/// Locus asking WMS to create a putaway job. <c>wsLocusAPI.p:195-236</c>.
/// </summary>
/// <remarks>
/// Routes to the fixed event name <c>PUTAWAYREQUEST</c> — unlike the other two wrappers,
/// the event is not read from the payload.
/// </remarks>
public sealed record LocusPutawayJobRequest
{
    /// <summary>
    /// SSCC-18 or tote id. Also the correlation key for the request archive, and the
    /// value the prefix rule in <c>wsLocusAPI.p:259-265</c> inspects.
    /// </summary>
    [JsonPropertyName("LicensePlate")]
    public string? LicensePlate { get; init; }

    [JsonPropertyName("RequestUser")]
    public string? RequestUser { get; init; }

    [JsonPropertyName("RequestRobot")]
    public string? RequestRobot { get; init; }

    [JsonPropertyName("RequestDate")]
    public string? RequestDate { get; init; }
}

/// <summary>
/// Putaway or replenishment result. <c>wsLocusAPI.p:237-300</c>.
/// </summary>
/// <remarks>
/// The event name is <b>prefixed</b> before dispatch: <c>replen</c> when the first
/// character of <see cref="LicensePlate"/> parses as an integer, <c>putaway</c> otherwise.
/// See <c>LocusEventRouter.ApplyLicencePlatePrefix</c>.
/// </remarks>
public sealed record LocusPutawayJobResult
{
    [JsonPropertyName("EventType")]
    public string? EventType { get; init; }

    /// <summary>Drives the putaway/replen routing decision.</summary>
    [JsonPropertyName("LicensePlate")]
    public string? LicensePlate { get; init; }

    [JsonPropertyName("JobStatus")]
    public string? JobStatus { get; init; }

    [JsonPropertyName("JobRobot")]
    public string? JobRobot { get; init; }

    [JsonPropertyName("JobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("JobDate")]
    public string? JobDate { get; init; }

    [JsonPropertyName("EventInfo")]
    public string? EventInfo { get; init; }

    [JsonPropertyName("JobTasks")]
    public LocusPutawayJobTasks? JobTasks { get; init; }
}

/// <summary>
/// Task wrapper on a putaway result. Unlike the order-job equivalent this <b>is</b> an
/// array — <c>locusAPI.cls:3271</c>.
/// </summary>
public sealed record LocusPutawayJobTasks
{
    [JsonPropertyName("PutawayJobResultTask")]
    public IReadOnlyList<LocusPutawayJobResultTask>? PutawayJobResultTask { get; init; }
}

/// <summary>One putaway task result. Fields from <c>locusAPI.cls:3277-3300</c>.</summary>
public sealed record LocusPutawayJobResultTask
{
    [JsonPropertyName("JobTaskId")]
    public string? JobTaskId { get; init; }

    /// <summary><c>PUT</c> is the value the ABL branches on at <c>locusAPI.cls:3283</c>.</summary>
    [JsonPropertyName("TaskType")]
    public string? TaskType { get; init; }

    [JsonPropertyName("TaskStatus")]
    public string? TaskStatus { get; init; }

    [JsonPropertyName("TaskLocation")]
    public string? TaskLocation { get; init; }

    [JsonPropertyName("TaskQty")]
    public string? TaskQty { get; init; }

    /// <summary>Quantity moved. Converted with <c>integer(...)</c> at <c>locusAPI.cls:3285</c>.</summary>
    [JsonPropertyName("ExecQty")]
    public string? ExecQty { get; init; }

    [JsonPropertyName("ExecUser")]
    public string? ExecUser { get; init; }

    [JsonPropertyName("ExecDate")]
    public string? ExecDate { get; init; }

    [JsonPropertyName("ItemNo")]
    public string? ItemNo { get; init; }

    [JsonPropertyName("ExceptionCode")]
    public string? ExceptionCode { get; init; }

    [JsonPropertyName("ExceptionReason")]
    public string? ExceptionReason { get; init; }
}
