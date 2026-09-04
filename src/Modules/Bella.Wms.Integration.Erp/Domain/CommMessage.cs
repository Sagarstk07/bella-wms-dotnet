namespace Bella.Wms.Integration.Erp.Domain;

/// <summary>
/// A row of <c>comm_out</c> or <c>comm_in</c> — the ERP interface message queue.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCHEMA-INFERRED.</b> The eighteen fields below were harvested from the ABL buffers
/// <c>b_comm_out</c> / <c>x_comm_out</c> in <c>oms_comm_out_route.p</c> and
/// <c>oms_comm_out_prep.p</c>, and their <c>comm_in</c> equivalents. Both tables carry
/// the same field set. Verify against the <c>wmscomm</c> <c>.df</c> dump.
/// </para>
/// <para>
/// This is the cleanest structure in the whole <c>api/</c> module: a durable queue with
/// an explicit status machine, no shared state, and no callback into the warehouse core.
/// It is why Slice 1 starts here.
/// </para>
/// </remarks>
public sealed record CommMessage
{
    /// <summary>SCHEMA-INFERRED: <c>comm_id</c>. Primary key.</summary>
    public required long CommId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>co_num</c>.</summary>
    public required string Company { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>wh_num</c>.</summary>
    public required string Warehouse { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>comm_endpoint</c>. The far system.
    /// Only <c>"OMS"</c> is registered today — <c>clobber.i</c> defines
    /// <c>OMS_ENDPOINT_NAME</c> and <c>WMSCommOutProcess.cls</c> has a single
    /// <c>when "OMS"</c> branch.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>event_type</c>. Selects the processor.
    /// Outbound values registered in <c>WMSCommOutProcess.cls</c>: <c>IRCODT</c>,
    /// <c>IRSHUP</c>, <c>IRPMUP</c>, <c>IRPIUP</c>, <c>IRORUP</c>.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>event_id</c>.</summary>
    public string? EventId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>event_data</c>. The payload. CLOB in ABL, so unbounded.</summary>
    public string? EventData { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_status</c>. See <see cref="CommStatus"/>.</summary>
    public required string ProcessStatus { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_date</c>.</summary>
    public DateTimeOffset? ProcessDate { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_time</c>. Stored separately from the date, as ABL does.</summary>
    public int? ProcessTime { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_duration</c>.</summary>
    public decimal? ProcessDuration { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_response</c>. The far system's response body.</summary>
    public string? ProcessResponse { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>process_response_status</c>. HTTP status from the far system.</summary>
    public int? ProcessResponseStatus { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>create_date</c>.</summary>
    public DateTimeOffset? CreateDate { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>ref_comm_id</c>. Links a response back to its request.</summary>
    public long? ReferenceCommId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>ref_id</c>. The WMS record this message concerns.</summary>
    public string? ReferenceId { get; init; }

    /// <summary>SCHEMA-INFERRED: <c>ref_id_type</c>. What kind of record <c>ref_id</c> names.</summary>
    public string? ReferenceIdType { get; init; }

    /// <summary>
    /// SCHEMA-INFERRED: <c>transmission_id</c>. Included in every outbound payload and
    /// echoed back, per <c>IWMSCommOutProcessor:Process</c>.
    /// </summary>
    public string? TransmissionId { get; init; }
}
