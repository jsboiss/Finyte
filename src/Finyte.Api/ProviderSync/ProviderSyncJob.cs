using Finyte.Data.ProviderSync;
using Quartz;

namespace Finyte.Api.ProviderSync;

[DisallowConcurrentExecution]
public sealed class ProviderSyncJob(
    IProviderSyncWorker providerSyncWorker,
    ILogger<ProviderSyncJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await providerSyncWorker.ProcessNext(context.CancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Queued provider sync processing failed.");
        }
    }
}
