namespace Bella.Wms.Integration.Partners.Domain;

/// <summary>Channel names used for routing and for the request archive layout.</summary>
public static class PartnerChannel
{
    /// <summary>Locus Robotics. ABL router <c>api/wms/wsLocusAPI.p</c>.</summary>
    public const string Locus = "locus";

    /// <summary>AWS EventBridge middleware / Accutech AutoBagger. ABL router <c>api/wms/wsMiddlewareAPI.p</c>.</summary>
    public const string Middleware = "middleware";

    /// <summary>Pyramid WMS. ABL router <c>api/wms/wsPyramidAPI.p</c>.</summary>
    public const string Pyramid = "pyramid";

    /// <summary>UPS. Outbound only — no inbound router exists.</summary>
    public const string Ups = "ups";
}

/// <summary>
/// The complete inbound event surface for Locus Robotics.
/// </summary>
/// <remarks>
/// <para>
/// <b>This list is the API contract, and it did not exist before.</b> The ABL has no
/// route table — <c>wsLocusAPI.p</c> reflects on whatever string arrives in
/// <c>EventType</c>. The valid names were documented only in two comment blocks
/// (<c>wsLocusAPI.p:141-157</c> and <c>245-257</c>), which do not agree with the methods
/// actually present on <c>locusAPI.cls</c>.
/// </para>
/// <para>
/// The 23 names below are the intersection: methods on <c>locusAPI.cls</c> with the
/// inbound signature <c>(input JsonObject, output sHttpStatus, output lcResponse)</c>.
/// Each one names its ABL line.
/// </para>
/// <para>
/// <b>Prefix rule.</b> For a <c>PutawayJobResult</c> payload, <c>wsLocusAPI.p:259-265</c>
/// takes the first character of <c>LicensePlate</c>, tries to parse it as an integer, and
/// prefixes the event name with <c>putaway</c> when it fails or <c>replen</c> when it
/// succeeds. So a licence plate starting with a letter is a tote and routes to
/// <c>putawayPUT</c>; one starting with a digit routes to <c>replenPUT</c>. That rule is
/// implemented in the Locus router, not here.
/// </para>
/// </remarks>
public static class LocusEventType
{
    // --- Order job lifecycle (OrderJobResult payload) ---

    /// <summary><c>locusAPI.cls:1756</c>.</summary>
    public const string Accept = "ACCEPT";

    /// <summary><c>locusAPI.cls:1803</c>.</summary>
    public const string Reject = "REJECT";

    /// <summary><c>locusAPI.cls:1878</c>.</summary>
    public const string HoldComplete = "HOLDCOMPLETE";

    /// <summary><c>locusAPI.cls:1884</c>.</summary>
    public const string HoldReject = "HOLDREJECT";

    /// <summary><c>locusAPI.cls:1958</c>.</summary>
    public const string HoldReleaseComplete = "HOLDRELEASECOMPLETE";

    /// <summary><c>locusAPI.cls:1974</c>.</summary>
    public const string HoldReleaseReject = "HOLDRELEASEREJECT";

    /// <summary><c>locusAPI.cls:2048</c>.</summary>
    public const string UpdateComplete = "UPDATECOMPLETE";

    /// <summary><c>locusAPI.cls:2064</c>.</summary>
    public const string UpdateReject = "UPDATEREJECT";

    /// <summary><c>locusAPI.cls:2080</c>.</summary>
    public const string CancelComplete = "CANCELCOMPLETE";

    /// <summary><c>locusAPI.cls:2096</c>.</summary>
    public const string CancelReject = "CANCELREJECT";

    // --- Tote handling ---

    /// <summary><c>locusAPI.cls:2170</c>.</summary>
    public const string ToteInduct = "TOTEINDUCT";

    /// <summary><c>locusAPI.cls:2347</c>.</summary>
    public const string ToteMove = "TOTEMOVE";

    /// <summary>
    /// <c>locusAPI.cls:2469</c>. Present as a method but absent from the comment block
    /// at <c>wsLocusAPI.p:141-157</c> — an example of the two disagreeing.
    /// </summary>
    public const string ToteInductCancel = "TOTEINDUCTCANCEL";

    /// <summary>
    /// <c>locusAPI.cls:3188</c>. Named <c>testtotemove</c> in the ABL. A test hook
    /// reachable from production traffic because reflection dispatch does not
    /// distinguish it from a real endpoint. Not registered here — see
    /// <c>docs/ABL_CROSSREF.md</c>.
    /// </summary>
    public const string TestToteMove = "TESTTOTEMOVE";

    // --- Picking. DEFERRED: these reach into wms_webhost/wspick.p ---

    /// <summary>
    /// <c>locusAPI.cls:2544</c>. <b>Deferred.</b> Runs <c>wms_webhost/wspick.p</c>
    /// persistently inside an open transaction and passes a ROWID as a string.
    /// </summary>
    public const string Pick = "PICK";

    /// <summary><c>locusAPI.cls:3078</c>. <b>Deferred</b>, same reason.</summary>
    public const string PickComplete = "PICKCOMPLETE";

    // --- Putaway and replenishment (PutawayJobResult payload, prefixed) ---

    /// <summary><c>locusAPI.cls:3210</c>. Reached via a <c>PutawayJobRequest</c> payload.</summary>
    public const string PutawayRequest = "PUTAWAYREQUEST";

    /// <summary><c>locusAPI.cls:4495</c>. Prefixed form of <c>ACCEPT</c>.</summary>
    public const string PutawayAccept = "PUTAWAYACCEPT";

    /// <summary><c>locusAPI.cls:3895</c>. Prefixed form of <c>REJECT</c>.</summary>
    public const string PutawayReject = "PUTAWAYREJECT";

    /// <summary><c>locusAPI.cls:4503</c>.</summary>
    public const string PutawayPutInduct = "PUTAWAYPUTINDUCT";

    /// <summary><c>locusAPI.cls:3589</c>.</summary>
    public const string PutawayPut = "PUTAWAYPUT";

    /// <summary><c>locusAPI.cls:4487</c>.</summary>
    public const string PutawayPutComplete = "PUTAWAYPUTCOMPLETE";

    /// <summary><c>locusAPI.cls:3242</c>. The replen-prefixed completion.</summary>
    public const string ReplenPutComplete = "REPLENPUTCOMPLETE";
}

/// <summary>
/// The inbound event surface for the AWS EventBridge middleware / Accutech AutoBagger.
/// </summary>
/// <remarks>
/// <para>
/// <c>wsMiddlewareAPI.p</c> chooses the target class before dispatching: an
/// <c>operation</c> property routes to <c>accutechAutoBaggerIn</c> (line 218), while a
/// <c>LocalRequest</c> object routes to <c>accutechAutoBaggerOut</c> (line 260). So the
/// same event name can mean different things depending on the payload wrapper.
/// </para>
/// </remarks>
public static class AutoBaggerEventType
{
    /// <summary>
    /// Ship-complete callback. <c>accutechAutoBaggerIn.cls:107</c>.
    /// <b>Deferred</b> — runs <c>wms_webhost/wsship.p</c> persistently.
    /// </summary>
    public const string OrderComplete = "ORDCM";

    /// <summary>
    /// <c>accutechAutoBaggerOut.cls:125</c>. Defined as <c>PICK_COMPLETE_EVENT_TYPE</c>
    /// in <c>middleware.i</c>. <b>Deferred</b> — reaches the shipping engine.
    /// </summary>
    public const string PickComplete = "PickComplete";

    /// <summary><c>accutechAutoBaggerOut.cls:200</c>.</summary>
    public const string CartonPacked = "cartonPacked";

    /// <summary><c>accutechAutoBaggerOut.cls:501</c>.</summary>
    public const string IsValidPackage = "isValidPackage";

    /// <summary><c>accutechAutoBaggerOut.cls:570</c>.</summary>
    public const string GetAutoBaggerEligible = "getAutoBaggerEligible";

    /// <summary><c>accutechAutoBaggerOut.cls:593</c>.</summary>
    public const string GetLocusGrouping = "getLocusGrouping";

    /// <summary><c>accutechAutoBaggerOut.cls:684</c>.</summary>
    public const string GetTrackingLabel = "GetTrackingLabel";
}

/// <summary>
/// The inbound event surface for Pyramid WMS.
/// </summary>
/// <remarks>
/// <c>pyramidAPI.cls</c> has exactly one method with the inbound signature. The other
/// four public methods are outbound calls WMS makes into Pyramid.
/// </remarks>
public static class PyramidEventType
{
    /// <summary><c>pyramidAPI.cls:503</c>.</summary>
    public const string ShipOlpn = "SHIPOLPN";
}
