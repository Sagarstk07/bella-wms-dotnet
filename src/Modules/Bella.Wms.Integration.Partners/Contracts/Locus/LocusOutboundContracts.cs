using System.Text.Json.Serialization;

namespace Bella.Wms.Integration.Partners.Contracts.Locus;

/// <summary>
/// Envelope for an order job WMS sends to Locus. <c>locusAPI.cls:493-494</c>.
/// </summary>
public sealed record LocusOrderJobEnvelope
{
    [JsonPropertyName("OrderJob")]
    public required LocusOrderJob OrderJob { get; init; }
}

/// <summary>
/// An order job — the instruction to Locus to pick a carton.
/// </summary>
/// <remarks>
/// Recovered from <c>locusAPI.cls:createOrderJob</c> (362-521), with the priority and
/// single-unit fields from <c>buildJobPriorityGroup</c> (4615) and <c>buildSingleUnit</c>
/// (4806).
/// </remarks>
public sealed record LocusOrderJob
{
    /// <summary><c>"NEW"</c> for a create. <c>locusAPI.cls:399</c>.</summary>
    [JsonPropertyName("EventType")]
    public required string EventType { get; init; }

    /// <summary><c>cartonmst.carton_id</c>. <c>locusAPI.cls:400</c>.</summary>
    [JsonPropertyName("JobId")]
    public required string JobId { get; init; }

    /// <summary>
    /// ABL sends <c>iso-date(now)</c> (<c>locusAPI.cls:401</c>) — local time with offset,
    /// not UTC. Preserved as written; changing it changes what Locus records.
    /// </summary>
    [JsonPropertyName("JobDate")]
    public required string JobDate { get; init; }

    /// <summary>
    /// Set by <c>buildJobPriorityGroup</c> (<c>locusAPI.cls:4704</c>), derived from the
    /// carrier priority and cut-off tables in <c>locusConfig.json</c>, or overridden by
    /// the caller. Omitted when not set.
    /// </summary>
    [JsonPropertyName("JobPriority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobPriority { get; init; }

    /// <summary>
    /// Drop-off location from <c>getDropOffLocation</c> (<c>locusAPI.cls:5086</c>), which
    /// selects on <c>ordhdr.host_order_code</c> and <c>ordhdr.ship_country</c> (BEL-923).
    /// </summary>
    [JsonPropertyName("NextWorkArea")]
    public required string NextWorkArea { get; init; }

    /// <summary>
    /// Set by <c>buildSingleUnit</c> (<c>locusAPI.cls:4806</c>).
    /// <b>Sent as the string <c>"true"</c>, not a JSON boolean</b> — the ABL writes
    /// <c>jsoOrderJob:add("SingleUnit":U, "true":U)</c>. Kept as a string so the wire
    /// format is unchanged; a boolean here would be a silent contract change.
    /// </summary>
    [JsonPropertyName("SingleUnit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SingleUnit { get; init; }

    [JsonPropertyName("JobTasks")]
    public required LocusOrderJobTaskList JobTasks { get; init; }
}

/// <summary>
/// Task list wrapper. Outbound this <b>is</b> an array — <c>locusAPI.cls:490</c> —
/// even though the inbound result reads a single object. See
/// <see cref="LocusOrderJobTasks"/>.
/// </summary>
public sealed record LocusOrderJobTaskList
{
    [JsonPropertyName("OrderJobTask")]
    public required IReadOnlyList<LocusOrderJobTask> OrderJobTask { get; init; }
}

/// <summary>
/// One pick instruction. Fields from <c>locusAPI.cls:460-486</c>, one per <c>pick</c> row.
/// </summary>
public sealed record LocusOrderJobTask
{
    /// <summary><c>pick.id</c>.</summary>
    [JsonPropertyName("JobTaskId")]
    public required string JobTaskId { get; init; }

    /// <summary><c>cartonmst.order + "-" + cartonmst.order_suffix</c>. <c>locusAPI.cls:461</c>.</summary>
    [JsonPropertyName("OrderId")]
    public required string OrderId { get; init; }

    [JsonPropertyName("OrderType")]
    public string? OrderType { get; init; }

    /// <summary>Same value as <c>NextWorkArea</c> on the parent job (BEL0100014).</summary>
    [JsonPropertyName("CustOwner")]
    public string? CustOwner { get; init; }

    /// <summary>Always <c>"PICK"</c> here. <c>locusAPI.cls:464</c>.</summary>
    [JsonPropertyName("TaskType")]
    public required string TaskType { get; init; }

    /// <summary><c>pick.bin_num</c>.</summary>
    [JsonPropertyName("TaskLocation")]
    public string? TaskLocation { get; init; }

    /// <summary><c>pick.wh_zone</c> (BEL010021).</summary>
    [JsonPropertyName("TaskZone")]
    public string? TaskZone { get; init; }

    /// <summary><c>pick.qty</c>. Numeric outbound, unlike the inbound string quantities.</summary>
    [JsonPropertyName("TaskQty")]
    public decimal TaskQty { get; init; }

    /// <summary><c>pick.abs_num</c>.</summary>
    [JsonPropertyName("ItemNo")]
    public string? ItemNo { get; init; }

    /// <summary>UPC from <c>venddetail</c>.</summary>
    [JsonPropertyName("ItemUPC")]
    public string? ItemUPC { get; init; }

    /// <summary><c>item.item_desc</c>, trimmed.</summary>
    [JsonPropertyName("ItemDesc")]
    public string? ItemDesc { get; init; }

    /// <summary><c>item.length</c>.</summary>
    [JsonPropertyName("ItemLength")]
    public decimal ItemLength { get; init; }

    /// <summary><c>item.width</c>.</summary>
    [JsonPropertyName("ItemWidth")]
    public decimal ItemWidth { get; init; }

    /// <summary><c>item.height</c>.</summary>
    [JsonPropertyName("ItemHeight")]
    public decimal ItemHeight { get; init; }

    /// <summary><c>item.weight</c>.</summary>
    [JsonPropertyName("ItemWeight")]
    public decimal ItemWeight { get; init; }

    /// <summary>
    /// A real JSON boolean here (<c>locusAPI.cls:485</c>) — note the contrast with
    /// <see cref="LocusOrderJob.SingleUnit"/>, which is the string <c>"true"</c>.
    /// Added only for DHL, globale, purltr, and Shipium US-territory shipments; omitted
    /// otherwise rather than sent as false.
    /// </summary>
    [JsonPropertyName("CaptureSerialNo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CaptureSerialNo { get; init; }
}

/// <summary>Envelope for a putaway job. <c>locusAPI.cls:createPutawayJob</c> (992-1293).</summary>
public sealed record LocusPutawayJobEnvelope
{
    [JsonPropertyName("PutawayJob")]
    public required LocusPutawayJob PutawayJob { get; init; }
}

/// <summary>A putaway or replenishment job sent to Locus.</summary>
public sealed record LocusPutawayJob
{
    [JsonPropertyName("EventType")]
    public required string EventType { get; init; }

    /// <summary>Present on putaway jobs but not on order jobs.</summary>
    [JsonPropertyName("EventAction")]
    public string? EventAction { get; init; }

    [JsonPropertyName("JobId")]
    public required string JobId { get; init; }

    [JsonPropertyName("JobDate")]
    public required string JobDate { get; init; }

    [JsonPropertyName("JobPriority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JobPriority { get; init; }

    /// <summary>SSCC-18 or tote id.</summary>
    [JsonPropertyName("LicensePlate")]
    public string? LicensePlate { get; init; }

    /// <summary>Echoed back on the corresponding <see cref="LocusPutawayJobRequest"/>.</summary>
    [JsonPropertyName("RequestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("CustOwner")]
    public string? CustOwner { get; init; }

    [JsonPropertyName("JobTasks")]
    public required LocusPutawayJobTaskList JobTasks { get; init; }
}

/// <summary>Task list wrapper for a putaway job.</summary>
public sealed record LocusPutawayJobTaskList
{
    [JsonPropertyName("PutawayJobTask")]
    public required IReadOnlyList<LocusPutawayJobTask> PutawayJobTask { get; init; }
}

/// <summary>
/// One putaway instruction. Carries four <c>Custom</c> slots the order-job task does not.
/// </summary>
public sealed record LocusPutawayJobTask
{
    [JsonPropertyName("JobTaskId")]
    public required string JobTaskId { get; init; }

    [JsonPropertyName("TaskType")]
    public required string TaskType { get; init; }

    [JsonPropertyName("TaskLocation")]
    public string? TaskLocation { get; init; }

    [JsonPropertyName("TaskZone")]
    public string? TaskZone { get; init; }

    [JsonPropertyName("TaskWorkArea")]
    public string? TaskWorkArea { get; init; }

    /// <summary>Travel priority — putaway-specific.</summary>
    [JsonPropertyName("TaskTravelPriority")]
    public string? TaskTravelPriority { get; init; }

    [JsonPropertyName("TaskQty")]
    public decimal TaskQty { get; init; }

    [JsonPropertyName("ItemNo")]
    public string? ItemNo { get; init; }

    [JsonPropertyName("ItemUPC")]
    public string? ItemUPC { get; init; }

    [JsonPropertyName("ItemDesc")]
    public string? ItemDesc { get; init; }

    [JsonPropertyName("ItemLength")]
    public decimal ItemLength { get; init; }

    [JsonPropertyName("ItemWidth")]
    public decimal ItemWidth { get; init; }

    [JsonPropertyName("ItemHeight")]
    public decimal ItemHeight { get; init; }

    [JsonPropertyName("ItemWeight")]
    public decimal ItemWeight { get; init; }

    /// <summary>Purpose not determinable from the ABL alone — needs captured traffic.</summary>
    [JsonPropertyName("Custom1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Custom1 { get; init; }

    /// <inheritdoc cref="Custom1"/>
    [JsonPropertyName("Custom2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Custom2 { get; init; }

    /// <inheritdoc cref="Custom1"/>
    [JsonPropertyName("Custom3")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Custom3 { get; init; }

    /// <inheritdoc cref="Custom1"/>
    [JsonPropertyName("Custom4")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Custom4 { get; init; }
}

/// <summary>
/// What Locus returns to our outbound calls.
/// </summary>
/// <remarks>
/// <para>
/// Recovered from <c>locusAPI.cls:4007-4016</c> and the <c>ttLocResp</c> temp-table
/// definition at lines 107-111.
/// </para>
/// <para>
/// <b>Note the field-name asymmetry inside the ABL itself.</b> The temp-table declares
/// <c>field code</c> and <c>field errMessage ... serialize-name "message"</c> — lower
/// case — but the parse at line 4012 reads <c>"Code"</c> and <c>"Message"</c> with
/// capitals. So the ABL serialises one casing and deserialises another. Only the parse
/// side is exercised against real Locus traffic, so the capitalised form is what this
/// contract uses. Confirm against captured responses.
/// </para>
/// </remarks>
public sealed record LocusResponseEnvelope
{
    [JsonPropertyName("LocResp")]
    public LocusResponse? LocResp { get; init; }
}

/// <summary>The Locus response body.</summary>
public sealed record LocusResponse
{
    /// <summary>
    /// Status code. <c>locusAPI.cls:4025</c> treats anything not beginning <c>"2"</c> as
    /// a failure — a string prefix test, not a numeric comparison, so it is kept as a
    /// string here.
    /// </summary>
    [JsonPropertyName("Code")]
    public string? Code { get; init; }

    [JsonPropertyName("Message")]
    public string? Message { get; init; }

    /// <summary>True when the code begins with "2", matching <c>locusAPI.cls:4025</c>.</summary>
    public bool IsSuccess => Code?.StartsWith('2') == true;
}
