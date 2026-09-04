using System.Text.Json;
using Bella.Wms.Integration.Partners.Contracts;
using Bella.Wms.Integration.Partners.Domain;
using Microsoft.Extensions.Logging;

namespace Bella.Wms.Integration.Partners.Application.Locus;

/// <summary>Base for Locus inbound handlers.</summary>
public abstract class LocusEventHandlerBase : IInboundEventHandler
{
    protected LocusEventHandlerBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected ILogger Logger { get; }

    /// <inheritdoc />
    public string Channel => PartnerChannel.Locus;

    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public abstract Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a string property, or <see langword="null"/> if absent or not a JSON string.</summary>
    protected static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// Base for the Locus handlers that cannot be converted until picking and shipping move.
/// </summary>
/// <remarks>
/// <para>
/// These are not unwritten because they are hard to model. They are unwritten because the
/// ABL reaches out of the <c>api/</c> module and into the warehouse core through a
/// persistent procedure handle, and that call has no equivalent across any seam:
/// </para>
/// <code>
/// run wms_webhost/wspick.p persistent set hproc.
/// run PickData in hproc (input string(rowid(pick)), ...).
/// run DoPick   in hproc (output table taction, ...).
/// </code>
/// <para>
/// (<c>locusAPI.cls:2954-2984</c>.) <c>PickData</c> and <c>DoPick</c> take no parameters
/// of their own beyond a ROWID — a physical database address — and pass state to each
/// other through the persistent procedure instance. It happens inside an open
/// <c>PICK-BLOCK</c> transaction, which also breaks Phase 6 §4 rule 3.
/// </para>
/// <para>
/// Phase 6 §3 puts B06 Wave/Allocation live last because it has four times the lock
/// density of anything else. The same reasoning applies one module earlier: these
/// handlers wait for <c>Bella.Wms.Outbound.Picking</c> and
/// <c>Bella.Wms.Outbound.Shipping</c> to exist, or for an explicit decision to build an
/// ABL callback bridge.
/// </para>
/// </remarks>
public abstract class DeferredLocusEventHandler : LocusEventHandlerBase
{
    protected DeferredLocusEventHandler(ILogger logger)
        : base(logger)
    {
    }

    /// <summary>The ABL method and line range this handler will convert.</summary>
    protected abstract string AblSource { get; }

    /// <summary>Which warehouse-core procedure the ABL reaches into.</summary>
    protected abstract string WarehouseCoreDependency { get; }

    public sealed override Task<InboundEventResult> HandleAsync(
        InboundEventRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            $"{EventType} is deferred. Converts {AblSource}, which calls into " +
            $"{WarehouseCoreDependency}. Blocked until that module is converted or a " +
            "callback bridge is agreed. See docs/ABL_CROSSREF.md.");
}
