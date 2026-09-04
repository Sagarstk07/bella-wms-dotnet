using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Domain;
using Bella.Wms.Platform.Context;
using Microsoft.Extensions.Options;

namespace Bella.Wms.Jobs;

/// <summary>Which company, warehouse and endpoint a poller instance serves.</summary>
/// <remarks>
/// Replaces the <c>SESSION:PARAM</c> parsing in
/// <c>api/wms/interface/cron_globals.i:26-30</c>, which splits a comma-separated string
/// into <c>g_process_name_</c>, <c>g_company_</c>, <c>g_warehouse_</c> and
/// <c>g_api_name_</c>.
/// </remarks>
public sealed class CommWorkerOptions
{
    public const string SectionName = "CommWorker";

    /// <remarks>
    /// Not <c>required</c> — a type with required members cannot satisfy the
    /// <c>new()</c> constraint on <c>Configure&lt;TOptions&gt;</c> (CS9040).
    /// <see cref="Validate"/> enforces it at startup.
    /// </remarks>
    public string Company { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    /// <summary>ABL <c>g_api_name_</c>, matched against <c>comm_endpoint</c>.</summary>
    public string Endpoint { get; init; } = CommStatus.OmsEndpoint;

    /// <summary>ABL <c>IDLE-WAIT-MAX</c> — how long to sleep when the queue is empty.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to handle before yielding.</summary>
    public int BatchSize { get; init; } = 25;

    /// <summary>
    /// Fails at startup rather than polling forever against an empty company/warehouse.
    /// </summary>
    /// <remarks>
    /// The ABL equivalent is <c>cron_globals.i:32-38</c>, which treats <c>"*"</c> for
    /// either value as "not an application process" and carries on. A poller with no
    /// warehouse has nothing to do, so failing is the honest response.
    /// </remarks>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Company) || string.IsNullOrWhiteSpace(Warehouse))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Company and {SectionName}:Warehouse must both be set. " +
                "Each poller instance serves one company/warehouse pair, matching the " +
                "SESSION:PARAM contract in api/wms/interface/cron_globals.i.");
        }
    }
}

/// <summary>
/// Polls <c>comm_out</c> and routes each message to its processor.
/// </summary>
/// <remarks>
/// <para>
/// Converts the <c>MAIN-LOOP</c> in <c>api/wms/interface/oms_comm_out_route.p</c>
/// (386 lines) — a <c>do while true</c> loop running as a long-lived Progress session
/// launched by cron.
/// </para>
/// <para>
/// <b>What the hosted service replaces.</b> The ABL loop needs four include files to
/// manage its own lifecycle: <c>cron_globals.i</c> parses session parameters,
/// <c>cron_connect.i</c> writes a PID file and resolves paths, <c>cron_check.i</c> polls
/// for a stop file on disk, and <c>cron_disconnect.i</c> cleans up. All four disappear —
/// <see cref="BackgroundService"/> supplies the lifecycle and
/// <see cref="CancellationToken"/> replaces the stop-file check.
/// </para>
/// <para>
/// <b>Preserved.</b> <c>oms_comm_out_route.p:40</c> opens with
/// <c>pause random(1,5)</c> so that multiple pollers do not march in lockstep. The same
/// jitter is applied here for the same reason.
/// </para>
/// </remarks>
public sealed class CommOutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CommWorkerOptions _options;
    private readonly ILogger<CommOutWorker> _logger;

    public CommOutWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CommWorkerOptions> options,
        ILogger<CommOutWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        // ABL oms_comm_out_route.p:40 — pause random(1,5).
        await Task.Delay(
            TimeSpan.FromSeconds(Random.Shared.Next(1, 6)), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation(
            "comm_out poller started for {Company}/{Warehouse} endpoint {Endpoint}.",
            _options.Company, _options.Warehouse, _options.Endpoint);

        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;

            try
            {
                // A scope per pass: the unit of work, the context and the request-scoped
                // config cache all have request lifetimes, and a long-lived poller must
                // not hold one open across passes.
                await using var scope = _scopeFactory.CreateAsyncScope();

                var context = scope.ServiceProvider.GetRequiredService<WmsContext>();
                context.Initialise(
                    _options.Company,
                    _options.Warehouse,
                    userId: "COMMOUT",
                    userType: "SYSTEM");
                context.ApplyOrigin("api/wms/interface/oms_comm_out_route.p");

                var router = scope.ServiceProvider.GetRequiredService<CommOutRouter>();

                handled = await router.DrainAsync(
                    _options.Company,
                    _options.Warehouse,
                    _options.Endpoint,
                    _options.BatchSize,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad pass must not kill the poller — the ABL loop has the same
                // posture via its ON ERROR UNDO, LEAVE handling.
                _logger.LogError(ex, "comm_out poller pass failed. Continuing.");
            }

            if (handled == 0)
            {
                await Task.Delay(_options.IdleDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("comm_out poller stopping for {Company}/{Warehouse}.",
            _options.Company, _options.Warehouse);
    }
}
