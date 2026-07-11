using Finyte.Core.ProviderSync;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.ProviderSync;

public interface IProviderSyncWorker
{
    Task<bool> ProcessNext(CancellationToken cancellationToken);
}

public sealed class ProviderSyncWorker(
    FinyteDbContext dbContext,
    IProviderSyncRunner providerSyncRunner) : IProviderSyncWorker
{
    public async Task<bool> ProcessNext(CancellationToken cancellationToken)
    {
        var syncRunId = await dbContext.ProviderSyncRuns
            .AsNoTracking()
            .Where(x => x.Status == ProviderSyncStatus.Queued)
            .OrderBy(x => x.Dataset == ProviderSyncDataset.Accounts ? 0 : x.Dataset == ProviderSyncDataset.Balances ? 1 : 2)
            .ThenBy(x => x.CreatedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (syncRunId is null)
        {
            return false;
        }

        await providerSyncRunner.Run(syncRunId.Value, cancellationToken);
        return true;
    }
}
