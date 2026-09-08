using Microsoft.Extensions.DependencyInjection;
using Temporalio.Activities;

namespace Finyte.Data.ProviderSync;

public sealed class ProviderSyncActivities(IServiceScopeFactory serviceScopeFactory)
{
    [Activity]
    public async Task Run(Guid syncRunId)
    {
        ActivityExecutionContext.Current.Heartbeat(syncRunId);
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var providerSyncRunner = scope.ServiceProvider.GetRequiredService<IProviderSyncRunner>();
        await providerSyncRunner.Run(syncRunId, ActivityExecutionContext.Current.CancellationToken);
    }
}
