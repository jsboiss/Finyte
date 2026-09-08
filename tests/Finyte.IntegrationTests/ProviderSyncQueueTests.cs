using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProviderSyncQueueTests
{
    [Fact]
    public async Task InitialSyncCreatesOrderedDatasetsInOneTemporalBatch()
    {
        await using var dbContext = CreateDbContext();
        var connection = new ProviderConnection
        {
            TenantId = Guid.NewGuid(),
            TenantMemberId = Guid.NewGuid(),
            EndUserId = "end-user",
            ConsentId = "consent",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var queue = new ProviderSyncQueue(dbContext);

        var batchId = await queue.EnqueueInitialSync(connection, CancellationToken.None);

        var runs = await dbContext.ProviderSyncRuns.OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Equal(3, runs.Count);
        Assert.All(runs, x => Assert.Equal(batchId, x.BatchId));
        Assert.Equal(
            [ProviderSyncDataset.Accounts, ProviderSyncDataset.Balances, ProviderSyncDataset.Transactions],
            runs.Select(x => x.Dataset));
        Assert.All(runs, x => Assert.Equal(ProviderSyncStatus.Queued, x.Status));
        Assert.All(runs, x => Assert.Null(x.DispatchedAt));
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FinyteDbContext(options);
    }
}
