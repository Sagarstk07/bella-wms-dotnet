namespace Bella.Wms.Integration.Erp.Domain;

/// <summary>
/// The <c>process_status</c> values on <c>comm_in</c> and <c>comm_out</c>.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed verbatim from <c>api/wms/rest/clobber.i</c>, which defines these as
/// preprocessor constants "to avoid hard coding". Both the IN and OUT ladders use the
/// same letters, so one set covers both.
/// </para>
/// <para>
/// Note that <c>PREP_ERROR</c> and <c>OUT_ERROR</c> are both <c>"E"</c> in the ABL —
/// the stage a row failed at is recoverable only from which status preceded it. That
/// ambiguity is preserved rather than fixed, because the ABL pollers and any operational
/// queries written against this table depend on the single letter.
/// </para>
/// </remarks>
public static class CommStatus
{
    /// <summary><c>CLOB_*_STATUS_0_CREATE</c> — row created, not yet queued.</summary>
    public const string Create = "X";

    /// <summary><c>CLOB_*_STATUS_1_NEW</c> — queued, awaiting preparation.</summary>
    public const string New = "N";

    /// <summary><c>CLOB_*_STATUS_2_PREP</c> — being prepared by the prep poller.</summary>
    public const string Preparing = "P";

    /// <summary><c>CLOB_*_STATUS_3_OPEN</c> — prepared, awaiting the route poller.</summary>
    public const string Open = "O";

    /// <summary>
    /// <c>CLOB_*_STATUS_3_PREP_ERROR</c> and <c>CLOB_*_STATUS_5_OUT_ERROR</c>.
    /// Both are <c>"E"</c> in the ABL — see the remarks on this class.
    /// </summary>
    public const string Error = "E";

    /// <summary><c>CLOB_*_STATUS_4_WIP</c> — claimed by a poller, in flight.</summary>
    public const string WorkInProgress = "W";

    /// <summary><c>CLOB_*_STATUS_5_COMPLETE</c> — done.</summary>
    public const string Complete = "C";

    /// <summary>The only endpoint registered today. <c>clobber.i</c> <c>OMS_ENDPOINT_NAME</c>.</summary>
    public const string OmsEndpoint = "OMS";
}
