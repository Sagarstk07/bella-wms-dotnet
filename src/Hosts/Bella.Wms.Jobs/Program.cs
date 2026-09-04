using Bella.Wms.Integration.Erp.Application;
using Bella.Wms.Integration.Erp.Infrastructure;
using Bella.Wms.Jobs;
using Bella.Wms.Platform.Audit;
using Bella.Wms.Platform.Config;
using Bella.Wms.Platform.Data;
using Bella.Wms.Platform.Http;
using Bella.Wms.Platform.Identity;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpenEdgeOptions>(
    builder.Configuration.GetSection(OpenEdgeOptions.SectionName));
builder.Services.Configure<RequestArchiveOptions>(
    builder.Configuration.GetSection(RequestArchiveOptions.SectionName));
builder.Services.Configure<Fdm4Options>(
    builder.Configuration.GetSection(Fdm4Options.SectionName));
builder.Services.Configure<CommWorkerOptions>(
    builder.Configuration.GetSection(CommWorkerOptions.SectionName));

builder.Services.AddWmsPlatform();
builder.Services.AddWmsConfig();
builder.Services.AddWmsAudit();
builder.Services.AddWmsHttp();

builder.Services.AddErpModule();

// Replaces the cron-launched persistent Progress session that ran
// api/wms/interface/oms_comm_out_route.p. The four cron_*.i includes that managed
// its lifecycle — PID files, stop-file polling, cleanup — are gone.
builder.Services.AddHostedService<CommOutWorker>();

var host = builder.Build();
host.Run();
