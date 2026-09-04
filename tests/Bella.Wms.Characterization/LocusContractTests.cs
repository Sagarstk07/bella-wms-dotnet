using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts.Locus;
using FluentAssertions;
using Xunit;

namespace Bella.Wms.Characterization;

/// <summary>
/// Contract tests for the Locus wire format.
/// </summary>
/// <remarks>
/// <para>
/// The payloads below are taken from <c>api/wms/locusResultTest.cls</c>, the ABL class
/// that generates mock Locus responses for integration testing. It is the closest thing
/// to a written specification of this interface that exists, because it has to produce
/// payloads the real handlers accept.
/// </para>
/// <para>
/// <b>These are not a substitute for captured traffic.</b> They prove our contracts match
/// what the ABL test harness produces, which is what the ABL <i>developers believed</i>
/// Locus sends. Replace them with recorded production payloads once the Phase 5 harness
/// has run for a week.
/// </para>
/// </remarks>
public sealed class LocusContractTests
{
    // The canonical Locus wire-format options. They live in Contracts, beside the DTOs
    // they apply to — not on LocusEventRouter, which does not serialize at all.
    private static readonly JsonSerializerOptions Options = LocusJson.SerializerOptions;

    /// <summary>
    /// The ACCEPT payload, byte-for-byte the shape
    /// <c>locusResultTest.cls:createOrderJobResultAccept</c> builds (lines 138-152).
    /// </summary>
    private const string AcceptPayload = """
        {
          "OrderJobResult": {
            "EventType": "ACCEPT",
            "JobId": "C0001234",
            "JobStatus": "COMPLETED",
            "JobDate": "2026-08-30T14:22:05.123+05:30"
          }
        }
        """;

    [Fact]
    public void AcceptPayloadBindsToTheContract()
    {
        var envelope = JsonSerializer.Deserialize<LocusInboundEnvelope>(AcceptPayload, Options);

        envelope.Should().NotBeNull();
        envelope!.OrderJobResult.Should().NotBeNull();
        envelope.OrderJobResult!.EventType.Should().Be("ACCEPT");
        envelope.OrderJobResult.JobId.Should().Be("C0001234");
        envelope.OrderJobResult.JobStatus.Should().Be("COMPLETED");

        // JobDate stays a string deliberately — the ABL never parses it, so nothing
        // establishes the format Locus actually sends.
        envelope.OrderJobResult.JobDate.Should().Be("2026-08-30T14:22:05.123+05:30");

        envelope.PutawayJobRequest.Should().BeNull();
        envelope.PutawayJobResult.Should().BeNull();
    }

    /// <summary>
    /// A pick result with its nested task.
    /// </summary>
    /// <remarks>
    /// Note <c>OrderJobResultTask</c> is a single object, not an array — this is what
    /// <c>locusAPI.cls:2621</c> reads with <c>getjsonobject</c>. See the remarks on
    /// <see cref="LocusOrderJobTasks"/>.
    /// </remarks>
    private const string PickPayload = """
        {
          "OrderJobResult": {
            "EventType": "PICK",
            "JobId": "C0001234",
            "JobStatus": "INPROGRESS",
            "JobDate": "2026-08-30T14:25:00+05:30",
            "JobRobot": "BOT-17",
            "JobTasks": {
              "OrderJobResultTask": {
                "JobTaskId": "998877",
                "OrderId": "SO12345-01",
                "TaskType": "PICK",
                "TaskStatus": "COMPLETED",
                "TaskLocation": "A-01-02-03",
                "TaskQty": "4",
                "ExecQty": "4",
                "ExecUser": "LOCUSOP1",
                "ExecDate": "2026-08-30T14:24:55+05:30",
                "ItemNo": "3001",
                "SerialNo": "CN-0099"
              }
            }
          }
        }
        """;

    [Fact]
    public void PickPayloadBindsIncludingTheNestedTask()
    {
        var envelope = JsonSerializer.Deserialize<LocusInboundEnvelope>(PickPayload, Options);

        var task = envelope!.OrderJobResult!.JobTasks!.OrderJobResultTask;

        task.Should().NotBeNull();
        task!.JobTaskId.Should().Be("998877");
        task.OrderId.Should().Be("SO12345-01");
        task.ExecUser.Should().Be("LOCUSOP1");
        task.ItemNo.Should().Be("3001");
        task.SerialNo.Should().Be("CN-0099");

        // Quantities are strings on the wire. The ABL parses them as text and converts
        // at the point of use with decimal(...) / integer(...), so a non-numeric value
        // fails there rather than at parse time. Binding them as numbers here would move
        // the failure earlier and change behaviour.
        task.TaskQty.Should().Be("4");
        task.ExecQty.Should().Be("4");
    }

    /// <summary>
    /// A putaway result. The task wrapper here <b>is</b> an array —
    /// <c>locusAPI.cls:3271</c> uses <c>getjsonarray</c>, unlike the order-job path.
    /// </summary>
    private const string PutawayResultPayload = """
        {
          "PutawayJobResult": {
            "EventType": "PUTCOMPLETE",
            "LicensePlate": "TOTE0099",
            "JobStatus": "COMPLETED",
            "JobRobot": "BOT-04",
            "JobTasks": {
              "PutawayJobResultTask": [
                { "JobTaskId": "1", "TaskType": "PUT", "ExecQty": "12", "TaskLocation": "B-02-01-01" },
                { "JobTaskId": "2", "TaskType": "PUT", "ExecQty": "8",  "TaskLocation": "B-02-01-02" }
              ]
            }
          }
        }
        """;

    [Fact]
    public void PutawayResultTaskIsAnArrayUnlikeTheOrderJobEquivalent()
    {
        var envelope = JsonSerializer.Deserialize<LocusInboundEnvelope>(PutawayResultPayload, Options);

        var tasks = envelope!.PutawayJobResult!.JobTasks!.PutawayJobResultTask;

        tasks.Should().NotBeNull();
        tasks.Should().HaveCount(2, "locusAPI.cls:3273 loops over the array length");
        tasks![0].TaskType.Should().Be("PUT");
        tasks[1].ExecQty.Should().Be("8");
    }

    /// <summary>
    /// The asymmetry between the two task wrappers, asserted so it cannot be "tidied up"
    /// by someone who assumes it is a mistake.
    /// </summary>
    /// <remarks>
    /// <c>OrderJobResultTask</c> is read with <c>getjsonobject</c> (single) at
    /// <c>locusAPI.cls:2621</c>; <c>PutawayJobResultTask</c> with <c>getjsonarray</c> at
    /// line 3271. Whether Locus ever sends multiple order-job tasks is an open question
    /// for the captured traffic — if it does, the current ABL silently processes only the
    /// first, because the call is wrapped in <c>NO-ERROR</c>.
    /// </remarks>
    [Fact]
    public void OrderJobTaskIsSingularAndPutawayTaskIsPlural()
    {
        typeof(LocusOrderJobTasks)
            .GetProperty(nameof(LocusOrderJobTasks.OrderJobResultTask))!
            .PropertyType
            .Should().Be<LocusOrderJobResultTask>(
                "locusAPI.cls:2621 reads OrderJobResultTask with getjsonobject, not getjsonarray");

        typeof(LocusPutawayJobTasks)
            .GetProperty(nameof(LocusPutawayJobTasks.PutawayJobResultTask))!
            .PropertyType
            .Should().Be<IReadOnlyList<LocusPutawayJobResultTask>>(
                "locusAPI.cls:3271 reads PutawayJobResultTask with getjsonarray");
    }

    /// <summary>
    /// A putaway <i>request</i> — the third envelope branch, which routes to a fixed
    /// event name rather than reading one from the payload.
    /// </summary>
    [Fact]
    public void PutawayJobRequestBindsAndCarriesTheLicencePlate()
    {
        const string payload = """
            {
              "PutawayJobRequest": {
                "LicensePlate": "00123456789012345678",
                "RequestUser": "LOCUSOP2",
                "RequestRobot": "BOT-09",
                "RequestDate": "2026-08-30T15:00:00+05:30"
              }
            }
            """;

        var envelope = JsonSerializer.Deserialize<LocusInboundEnvelope>(payload, Options);

        envelope!.PutawayJobRequest.Should().NotBeNull();
        envelope.PutawayJobRequest!.LicensePlate.Should().Be("00123456789012345678");
        envelope.PutawayJobRequest.RequestRobot.Should().Be("BOT-09");
    }

    /// <summary>
    /// Outbound: <c>SingleUnit</c> must serialise as the string <c>"true"</c>, and
    /// <c>CaptureSerialNo</c> as a real boolean. The ABL is inconsistent between them
    /// (<c>locusAPI.cls:4806</c> vs <c>485</c>) and Locus has been receiving that
    /// inconsistency for years.
    /// </summary>
    [Fact]
    public void OutboundKeepsTheAblBooleanInconsistency()
    {
        var job = new LocusOrderJob
        {
            EventType = "NEW",
            JobId = "C0001234",
            JobDate = "2026-08-30T14:00:00+05:30",
            NextWorkArea = "ALOB2C",
            SingleUnit = "true",
            JobTasks = new LocusOrderJobTaskList
            {
                OrderJobTask =
                [
                    new LocusOrderJobTask
                    {
                        JobTaskId = "998877",
                        OrderId = "SO12345-01",
                        TaskType = "PICK",
                        TaskQty = 4m,
                        CaptureSerialNo = true,
                    },
                ],
            },
        };

        var json = JsonSerializer.Serialize(
            new LocusOrderJobEnvelope { OrderJob = job }, Options);

        json.Should().Contain("\"SingleUnit\":\"true\"",
            "the ABL sends SingleUnit as a string literal, not a JSON boolean");
        json.Should().Contain("\"CaptureSerialNo\":true",
            "CaptureSerialNo is a real boolean in the ABL");
        json.Should().Contain("\"OrderJob\":", "the payload is wrapped");
    }

    /// <summary>
    /// Optional outbound fields are omitted, not sent as null. The ABL only calls
    /// <c>add()</c> when the value applies, so a null would be a new thing on the wire.
    /// </summary>
    [Fact]
    public void UnsetOptionalFieldsAreOmittedNotNulled()
    {
        var job = new LocusOrderJob
        {
            EventType = "NEW",
            JobId = "C0001234",
            JobDate = "2026-08-30T14:00:00+05:30",
            NextWorkArea = "ALOB2C",
            JobTasks = new LocusOrderJobTaskList { OrderJobTask = [] },
        };

        var json = JsonSerializer.Serialize(
            new LocusOrderJobEnvelope { OrderJob = job }, Options);

        json.Should().NotContain("SingleUnit");
        json.Should().NotContain("JobPriority");
    }
}
