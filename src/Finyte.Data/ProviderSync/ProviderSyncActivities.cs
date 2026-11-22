using Microsoft.Extensions.DependencyInjection;
using Temporalio.Activities;

namespace Finyte.Data.ProviderSync;

public sealed class ProviderSyncActivities(IServiceScopeFactory serviceScopeFactory, TimeSpan? heartbeatInterval = null)
{
    private readonly TimeSpan interval = heartbeatInterval ?? TimeSpan.FromSeconds(15);

    [Activity]
    public async Task Run(Guid syncRunId)
    {
        var context = ActivityExecutionContext.Current;
        using var stop = new CancellationTokenSource();
        var heartbeating = Heartbeat(context, syncRunId, stop.Token);
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var providerSyncRunner = scope.ServiceProvider.GetRequiredService<IProviderSyncRunner>();
            await providerSyncRunner.Run(syncRunId, context.CancellationToken);
        }
        finally
        {
            await stop.CancelAsync();
            await heartbeating;
        }
    }

    private async Task Heartbeat(ActivityExecutionContext context, Guid syncRunId, CancellationToken cancellationToken)
    {
        context.Heartbeat(syncRunId);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                context.Heartbeat(syncRunId);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
