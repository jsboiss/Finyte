using Finyte.Data;
using Finyte.Data.ProviderSync;
using Finyte.Data.Analytics;
using Finyte.Data.Temporal;
using Finyte.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFinyteData(builder.Configuration);
builder.Services.AddFiskilProvider(builder.Configuration);
builder.Services
    .AddOptions<TemporalOptions>()
    .Bind(builder.Configuration.GetSection(TemporalOptions.SectionName));
builder.Services.AddSingleton<ProviderSyncActivities>();
builder.Services.AddSingleton<DashboardProjectionActivities>();
builder.Services.AddHostedService<TemporalWorkerService>();

await builder.Build().RunAsync();
