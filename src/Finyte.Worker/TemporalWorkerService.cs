using Finyte.Data.Analytics;
using Finyte.Data.ProviderSync;
using Finyte.Data.Temporal;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Worker;

namespace Finyte.Worker;

public sealed class TemporalWorkerService(
    IOptions<TemporalOptions> options,
    ProviderSyncActivities providerSyncActivities,
    DashboardProjectionActivities dashboardProjectionActivities,
    ILogger<TemporalWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var temporalOptions = options.Value;
        if (!temporalOptions.Enabled)
        {
            logger.LogInformation("Temporal worker is disabled.");
            return;
        }

        var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalOptions.Address)
        {
            Namespace = temporalOptions.Namespace
        });
        using var worker = new TemporalWorker(
            client,
            new TemporalWorkerOptions(temporalOptions.TaskQueue)
                .AddWorkflow<ProviderSyncWorkflow>()
                .AddWorkflow<DashboardProjectionWorkflow>()
                .AddActivity(providerSyncActivities.Run)
                .AddActivity(dashboardProjectionActivities.Rebuild));

        logger.LogInformation(
            "Temporal worker is polling task queue {TaskQueue} in namespace {Namespace}.",
            temporalOptions.TaskQueue,
            temporalOptions.Namespace);
        await worker.ExecuteAsync(stoppingToken);
    }
}
