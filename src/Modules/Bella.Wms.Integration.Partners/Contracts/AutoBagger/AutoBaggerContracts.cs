using System.Text.Json.Serialization;

namespace Bella.Wms.Integration.Partners.Contracts.AutoBagger;

/// <summary>
/// The envelope the AWS EventBridge middleware posts to us.
/// </summary>
/// <remarks>
/// <para>
/// Recovered from <c>api/wms/wsMiddlewareAPI.p:152-279</c>. Two shapes arrive on the same
/// endpoint and the router picks the handler class by which one it sees:
/// </para>
/// <list type="bullet">
///   <item>
///     An <c>operation</c> property plus a <c>Data</c> object routes to
///     <c>accutechAutoBaggerIn</c> (line 218) — this is the ship-complete callback.
///   </item>
///   <item>
///     A <c>LocalRequest</c> object routes to <c>accutechAutoBaggerOut</c> (line 260).
///   </item>
/// </list>
/// <para>
/// <b>Note the casing inconsistency.</b> The outer envelope uses lower-case
/// <c>operation</c> and <c>eventType</c> while everything nested uses <c>PascalCase</c>.
/// That is what the ABL reads, so it is preserved exactly.
/// </para>
/// </remarks>
public sealed record AutoBaggerInboundEnvelope
{
    /// <summary>
    /// Event name for the ship-complete branch, e.g. <c>ORDCM</c>.
    /// <c>wsMiddlewareAPI.p:156</c>.
    /// </summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    /// <summary>Wrapper present on the ship-complete branch. <c>wsMiddlewareAPI.p:161</c>.</summary>
    [JsonPropertyName("Data")]
    public AutoBaggerDataWrapper? Data { get; init; }

    /// <summary>
    /// Present on the outbound branch. The constant <c>LOCAL_INBOUND</c> in
    /// <c>middleware.i</c> resolves to <c>"LocalRequest"</c>.
    /// </summary>
    [JsonPropertyName("LocalRequest")]
    public AutoBaggerLocalRequest? LocalRequest { get; init; }

    /// <summary>
    /// Endpoint health check. <c>wsMiddlewareAPI.p:268</c> answers
    /// <c>-d '{"test": true}'</c> with HTTP 200 and "Caught the inbound request",
    /// bypassing all routing. Preserved — monitoring may depend on it.
    /// </summary>
    [JsonPropertyName("test")]
    public bool? Test { get; init; }
}

/// <summary>Wrapper around the order job result. <c>wsMiddlewareAPI.p:179</c>.</summary>
public sealed record AutoBaggerDataWrapper
{
    /// <summary>Constant <c>ORDER_JOB_RESULT</c> in <c>middleware.i</c> = <c>"OrderJobResult"</c>.</summary>
    [JsonPropertyName("OrderJobResult")]
    public AutoBaggerOrderJobResult? OrderJobResult { get; init; }
}

/// <summary>
/// Ship-complete result from the AutoBagger.
/// </summary>
/// <remarks>
/// <b>Company and warehouse come from the body, not the headers.</b>
/// <c>wsMiddlewareAPI.p:200-204</c> is explicit about this — "get company from JSON
/// payload not the header" — which is why the middleware route is excluded from the
/// header-based context middleware.
/// </remarks>
public sealed record AutoBaggerOrderJobResult
{
    /// <summary>The carton id. <c>wsMiddlewareAPI.p:192</c>.</summary>
    [JsonPropertyName("JobId")]
    public string? JobId { get; init; }

    /// <summary>Validated against <c>whmst</c>. <c>wsMiddlewareAPI.p:200</c>.</summary>
    [JsonPropertyName("CompanyID")]
    public string? CompanyId { get; init; }

    /// <summary>Validated against <c>whmst</c>. <c>wsMiddlewareAPI.p:204</c>.</summary>
    [JsonPropertyName("WhseID")]
    public string? WarehouseId { get; init; }

    [JsonPropertyName("JobStatus")]
    public string? JobStatus { get; init; }

    [JsonPropertyName("JobDate")]
    public string? JobDate { get; init; }

    [JsonPropertyName("JobRobot")]
    public string? JobRobot { get; init; }

    [JsonPropertyName("JobStation")]
    public string? JobStation { get; init; }

    [JsonPropertyName("JobMethod")]
    public string? JobMethod { get; init; }

    [JsonPropertyName("JobContainer")]
    public string? JobContainer { get; init; }

    [JsonPropertyName("JobWeight")]
    public string? JobWeight { get; init; }

    /// <summary>Carrier tracking number returned by the bagger.</summary>
    [JsonPropertyName("TrackingId")]
    public string? TrackingId { get; init; }

    /// <summary>ZPL label content. Tracked through <c>cartonmst.external_status</c>.</summary>
    [JsonPropertyName("ShipZpl")]
    public string? ShipZpl { get; init; }

    [JsonPropertyName("Carrier")]
    public string? Carrier { get; init; }

    [JsonPropertyName("Service")]
    public string? Service { get; init; }

    [JsonPropertyName("Exception")]
    public string? Exception { get; init; }

    [JsonPropertyName("ExceptionCode")]
    public string? ExceptionCode { get; init; }

    [JsonPropertyName("ExceptionReason")]
    public string? ExceptionReason { get; init; }

    /// <summary>
    /// Task detail. Note the property is <c>OrderJobResultTasks</c> — plural, unlike the
    /// Locus <c>OrderJobResultTask</c>. Same system family, different spelling.
    /// </summary>
    [JsonPropertyName("JobTasks")]
    public AutoBaggerJobTasks? JobTasks { get; init; }
}

/// <summary>Task wrapper on an AutoBagger result.</summary>
public sealed record AutoBaggerJobTasks
{
    [JsonPropertyName("OrderJobResultTasks")]
    public IReadOnlyList<AutoBaggerJobTask>? OrderJobResultTasks { get; init; }
}

/// <summary>One line of an AutoBagger result.</summary>
public sealed record AutoBaggerJobTask
{
    [JsonPropertyName("JobTaskId")]
    public string? JobTaskId { get; init; }

    [JsonPropertyName("OrderId")]
    public string? OrderId { get; init; }

    [JsonPropertyName("LineNbr")]
    public string? LineNumber { get; init; }

    [JsonPropertyName("ItemNo")]
    public string? ItemNo { get; init; }

    [JsonPropertyName("ItemUpc")]
    public string? ItemUpc { get; init; }

    [JsonPropertyName("ItemDesc")]
    public string? ItemDesc { get; init; }

    [JsonPropertyName("LotNo")]
    public string? LotNumber { get; init; }

    [JsonPropertyName("SerialNo")]
    public string? SerialNo { get; init; }

    [JsonPropertyName("TaskType")]
    public string? TaskType { get; init; }

    [JsonPropertyName("TaskStatus")]
    public string? TaskStatus { get; init; }

    [JsonPropertyName("TaskLocation")]
    public string? TaskLocation { get; init; }

    [JsonPropertyName("TaskQty")]
    public string? TaskQty { get; init; }

    [JsonPropertyName("ExecQty")]
    public string? ExecQty { get; init; }

    [JsonPropertyName("ExecUser")]
    public string? ExecUser { get; init; }

    [JsonPropertyName("ExecDate")]
    public string? ExecDate { get; init; }

    [JsonPropertyName("ExecRobot")]
    public string? ExecRobot { get; init; }
}

/// <summary>
/// The outbound-branch payload. <c>wsMiddlewareAPI.p:225-266</c>.
/// </summary>
/// <remarks>
/// Despite arriving on the inbound endpoint, this routes to
/// <c>accutechAutoBaggerOut</c> — the middleware calls back into WMS to ask for work,
/// rather than reporting completed work.
/// </remarks>
public sealed record AutoBaggerLocalRequest
{
    /// <summary>
    /// e.g. <c>PickComplete</c> — the constant <c>PICK_COMPLETE_EVENT_TYPE</c> in
    /// <c>middleware.i</c>. <c>wsMiddlewareAPI.p:241</c>.
    /// </summary>
    [JsonPropertyName("EventType")]
    public string? EventType { get; init; }

    /// <summary><c>wsMiddlewareAPI.p:245</c>.</summary>
    [JsonPropertyName("CompanyID")]
    public string? CompanyId { get; init; }

    /// <summary><c>wsMiddlewareAPI.p:249</c>.</summary>
    [JsonPropertyName("WhseID")]
    public string? WarehouseId { get; init; }

    [JsonPropertyName("OrderJobId")]
    public string? OrderJobId { get; init; }

    [JsonPropertyName("CustOwner")]
    public string? CustOwner { get; init; }

    [JsonPropertyName("CustCode")]
    public string? CustomerCode { get; init; }

    [JsonPropertyName("ShipCustCode")]
    public string? ShipCustomerCode { get; init; }

    [JsonPropertyName("ShipName")]
    public string? ShipName { get; init; }

    [JsonPropertyName("CustomerPo")]
    public string? CustomerPo { get; init; }

    [JsonPropertyName("OrderType")]
    public string? OrderType { get; init; }

    [JsonPropertyName("NbrLines")]
    public string? NumberOfLines { get; init; }

    /// <summary>Marks the last message of a batch.</summary>
    [JsonPropertyName("FinalFlag")]
    public string? FinalFlag { get; init; }

    [JsonPropertyName("JobEligible")]
    public string? JobEligible { get; init; }

    [JsonPropertyName("JobFlag")]
    public string? JobFlag { get; init; }

    [JsonPropertyName("JobPriority")]
    public string? JobPriority { get; init; }

    [JsonPropertyName("JobQty")]
    public string? JobQty { get; init; }

    [JsonPropertyName("ToteId")]
    public string? ToteId { get; init; }

    [JsonPropertyName("EventDate")]
    public string? EventDate { get; init; }

    // --- Ship-to address, flattened rather than nested ---

    [JsonPropertyName("DestinationAddressLine1")]
    public string? DestinationAddressLine1 { get; init; }

    [JsonPropertyName("DestinationAddressLine2")]
    public string? DestinationAddressLine2 { get; init; }

    [JsonPropertyName("DestinationCity")]
    public string? DestinationCity { get; init; }

    [JsonPropertyName("DestinationStateOrProvince")]
    public string? DestinationStateOrProvince { get; init; }

    [JsonPropertyName("DestinationPostalCode")]
    public string? DestinationPostalCode { get; init; }

    /// <summary>
    /// Drives the <c>shipCountrySpecial()</c> custOwner override in
    /// <c>accutechAutoBaggerOut.cls</c> (BEL-923).
    /// </summary>
    [JsonPropertyName("DestinationCountry")]
    public string? DestinationCountry { get; init; }

    [JsonPropertyName("Carrier")]
    public string? Carrier { get; init; }

    [JsonPropertyName("Service")]
    public string? Service { get; init; }
}

/// <summary>
/// The envelope WMS sends out to the AWS EventBridge middleware.
/// </summary>
/// <remarks>
/// <para>
/// From <c>accutechAutoBaggerOut.cls:1607-1611</c>. The outer three fields are
/// lower-case while the body uses PascalCase — that is the AWS-facing convention and it
/// is preserved.
/// </para>
/// </remarks>
public sealed record AutoBaggerOutboundEnvelope
{
    /// <summary>Always <c>"SALT"</c>. <c>accutechAutoBaggerOut.cls:1609</c>.</summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "SALT";

    /// <summary>e.g. <c>"ORDHD"</c>. <c>accutechAutoBaggerOut.cls:1610</c>.</summary>
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    /// <summary>
    /// <c>utc-now()</c> — the ABL comment says "for AWS", so EventBridge is matching on
    /// it. UTC here, unlike the local-with-offset timestamps everywhere else in this
    /// codebase.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("OrderJob")]
    public AutoBaggerOutboundOrderJob? OrderJob { get; init; }

    /// <summary>Builds an envelope with the timestamp in UTC as the ABL writes it.</summary>
    public static AutoBaggerOutboundEnvelope Create(
        string operation,
        AutoBaggerOutboundOrderJob orderJob,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(orderJob);

        return new AutoBaggerOutboundEnvelope
        {
            Operation = operation,
            Timestamp = (timestamp ?? DateTimeOffset.UtcNow)
                .UtcDateTime
                .ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            OrderJob = orderJob,
        };
    }
}

/// <summary>
/// Order header sent to the AutoBagger. <c>accutechAutoBaggerOut.cls:1616-1620</c>.
/// </summary>
/// <remarks>
/// Only the fields established from the ABL are modelled. The full ORDHD body is larger
/// and needs the <c>ordhdr</c> schema plus a captured sample — the rest arrives with the
/// AutoBagger conversion itself.
/// </remarks>
public sealed record AutoBaggerOutboundOrderJob
{
    [JsonPropertyName("EventType")]
    public required string EventType { get; init; }

    /// <summary><c>cartonmst.carton_id</c>.</summary>
    [JsonPropertyName("JobId")]
    public required string JobId { get; init; }

    /// <summary><c>ordhdr.order + "-" + ordhdr.order_suffix</c>.</summary>
    [JsonPropertyName("OrderId")]
    public string? OrderId { get; init; }

    /// <summary>
    /// <c>ordhdr.order + ordhdr.order_suffix + cartonmst.carton_id</c> — concatenated
    /// with <b>no</b> separator, unlike <see cref="OrderId"/>.
    /// </summary>
    [JsonPropertyName("OrderJobId")]
    public string? OrderJobId { get; init; }

    /// <summary>
    /// <c>"S"</c> for single unit, <c>"M"</c> for multi — the ABL writes
    /// <c>string(lSingleUnit,"S/M")</c> and comments that it mimics the Locus single-unit
    /// flag. Note Locus receives the string <c>"true"</c> for the same concept; the two
    /// integrations disagree on representation.
    /// </summary>
    [JsonPropertyName("JobFlag")]
    public string? JobFlag { get; init; }
}
