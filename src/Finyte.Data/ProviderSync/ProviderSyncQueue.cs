using Finyte.Core.ProviderSync;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.ProviderSync;

public interface IProviderSyncQueue
{
    Task<Guid> EnqueueInitialSync(ProviderConnection connection, CancellationToken cancellationToken);
}

public sealed class ProviderSyncQueue(FinyteDbContext dbContext) : IProviderSyncQueue
{
    public async Task<Guid> EnqueueInitialSync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.ConsentId))
        {
            throw new InvalidOperationException("A consent is required before initial sync can be queued.");
        }

        var datasets = new[]
        {
            ProviderSyncDataset.Accounts,
            ProviderSyncDataset.Balances,
            ProviderSyncDataset.Transactions
        };
        var existingDatasets = await dbContext.ProviderSyncRuns
            .Where(x => x.TenantId == connection.TenantId
                && x.Provider == connection.Provider
                && x.ConsentId == connection.ConsentId
                && datasets.Contains(x.Dataset)
                && (x.Status == ProviderSyncStatus.Queued || x.Status == ProviderSyncStatus.Running))
            .Select(x => x.Dataset)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var batchId = Guid.NewGuid();

        foreach (var dataset in datasets.Where(x => !existingDatasets.Contains(x)))
        {
            dbContext.ProviderSyncRuns.Add(new ProviderSyncRun
            {
                BatchId = batchId,
                TenantId = connection.TenantId,
                Provider = connection.Provider,
                Dataset = dataset,
                Status = ProviderSyncStatus.Queued,
                ConsentId = connection.ConsentId,
                EndUserId = connection.EndUserId,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return batchId;
    }
}
