namespace Bella.Wms.Platform.Context;

/// <summary>
/// The .NET equivalent of <c>wms_webhost.static_connect</c>, the ambient request
/// context the ABL system carries down the call stack.
/// </summary>
/// <remarks>
/// <para>
/// <b>This member list is measured, not guessed.</b> These are exactly the twelve
/// members of <c>static_connect</c> that <c>api/wms</c> reads or writes, counted
/// across the module:
/// </para>
/// <list type="table">
///   <listheader><term>Member</term><description>Uses in api/wms</description></listheader>
///   <item><term>getRule</term><description>15</description></item>
///   <item><term>warehouse</term><description>13</description></item>
///   <item><term>gsuserid</term><description>12</description></item>
///   <item><term>company</term><description>11</description></item>
///   <item><term>initConnect</term><description>8</description></item>
///   <item><term>user_type</term><description>7</description></item>
///   <item><term>deptnum</term><description>5</description></item>
///   <item><term>shiftnum</term><description>4</description></item>
///   <item><term>labelprinterid</term><description>2</description></item>
///   <item><term>isPASOE</term><description>2</description></item>
///   <item><term>transdate</term><description>1</description></item>
///   <item><term>screenid</term><description>1</description></item>
/// </list>
/// <para>
/// Phase 6 §10 item 5 calls for "the ambient context object — mirroring
/// <c>static_connect</c>, populated at the API boundary". This is it, and because
/// <c>api/wms</c> contains zero <c>DEFINE SHARED VARIABLE</c> declarations, the
/// surface really is this small — unlike <c>rf/</c>, which has 140.
/// </para>
/// <para>
/// <b>Coexistence obligation (Phase 6 §9).</b> While ABL and .NET run together, this
/// object must be populated identically to <c>static_connect</c> or converted code
/// takes different branches for reasons nobody will find quickly.
/// </para>
/// <para>
/// <c>isPASOE</c> is deliberately absent: it exists in ABL to decide whether
/// <c>wmswipeout.p</c> must walk <c>SESSION:FIRST-OBJECT</c> tearing down leaked
/// objects between requests. .NET scoped lifetimes make that unnecessary — see
/// <c>docs/ABL_CROSSREF.md</c>.
/// </para>
/// </remarks>
public interface IWmsContext
{
    /// <summary>ABL <c>static_connect:company</c> — <c>cmpmst.co_num</c>.</summary>
    string Company { get; }

    /// <summary>ABL <c>static_connect:warehouse</c> — <c>whmst.wh_num</c>.</summary>
    string Warehouse { get; }

    /// <summary>ABL <c>static_connect:gsuserid</c> — <c>empmst.emp_num</c>, upper-cased.</summary>
    string UserId { get; }

    /// <summary>ABL <c>static_connect:deptnum</c> — <c>empmst.dept_num</c>.</summary>
    int DepartmentNumber { get; }

    /// <summary>ABL <c>static_connect:shiftnum</c> — <c>empmst.shf_num</c>.</summary>
    int ShiftNumber { get; }

    /// <summary>ABL <c>static_connect:user_type</c>.</summary>
    string UserType { get; }

    /// <summary>
    /// ABL <c>static_connect:transdate</c>. Set explicitly at the API boundary
    /// (<c>wsMiddlewareAPI.p:305</c> assigns <c>today</c>).
    /// </summary>
    DateOnly TransactionDate { get; }

    /// <summary>
    /// ABL <c>static_connect:screenid</c>. Identifies the originating program for
    /// audit rows. <c>wsMiddlewareAPI.p:304</c> sets
    /// <c>"/api/wms/accutechAutoBagger.cls"</c>.
    /// </summary>
    string ScreenId { get; }

    /// <summary>ABL <c>static_connect:labelprinterid</c>.</summary>
    string? LabelPrinterId { get; }

    /// <summary>
    /// Distinguishes rows this stack wrote from rows ABL wrote, for the whole of the
    /// parallel run. Phase 6 §9 requires audit rows be "indistinguishable in shape,
    /// and distinguishable in origin". No ABL equivalent — this is new.
    /// </summary>
    string OriginStack { get; }

    /// <summary>Correlates one inbound request across logs, audit rows and outbound calls.</summary>
    string CorrelationId { get; }
}
