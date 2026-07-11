using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProviderSyncWorkerTests
{
    [Fact]
    public async Task WorkerProcessesAccountsBeforeDependentDatasets()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var transactions = CreateRun(tenantId, ProviderSyncDataset.Transactions);
        var accounts = CreateRun(tenantId, ProviderSyncDataset.Accounts);
        var balances = CreateRun(tenantId, ProviderSyncDataset.Balances);
        dbContext.ProviderSyncRuns.AddRange(transactions, accounts, balances);
        await dbContext.SaveChangesAsync();
        var runner = new RecordingProviderSyncRunner();
        var worker = new ProviderSyncWorker(dbContext, runner);

        var processed = await worker.ProcessNext(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(accounts.Id, Assert.Single(runner.SyncRunIds));
    }

    private static ProviderSyncRun CreateRun(Guid tenantId, string dataset)
    {
        return new ProviderSyncRun
        {
            TenantId = tenantId,
            Dataset = dataset,
            Status = ProviderSyncStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static FinyteDbContext CreateDbContext()
    {
        return new FinyteDbContext(new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
    }

    private sealed class RecordingProviderSyncRunner : IProviderSyncRunner
    {
        public List<Guid> SyncRunIds { get; } = [];

        public Task Run(Guid syncRunId, CancellationToken cancellationToken)
        {
            SyncRunIds.Add(syncRunId);
            return Task.CompletedTask;
        }
    }
}
