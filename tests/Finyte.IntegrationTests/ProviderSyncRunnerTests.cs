using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProviderSyncRunnerTests
{
    [Fact]
    public async Task RunExecutesDatasetSyncAndProjectionRefresh()
    {
        await using var dbContext = CreateDbContext();
        var syncRun = CreateSyncRun(ProviderSyncDataset.Transactions);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var bankingSyncService = new RecordingBankingSyncService();
        var projectionRefresher = new RecordingSyncProjectionRefresher(dbContext);
        var runner = new ProviderSyncRunner(dbContext, bankingSyncService, projectionRefresher);

        await runner.Run(syncRun.Id, CancellationToken.None);

        Assert.Equal(syncRun.Id, bankingSyncService.TransactionSyncRunId);
        Assert.Equal(syncRun.Id, projectionRefresher.SyncRunId);
        var updatedRun = await dbContext.ProviderSyncRuns.SingleAsync();
        Assert.Equal(ProviderSyncStatus.Succeeded, updatedRun.Status);
    }

    [Fact]
    public async Task RunMarksSyncRunFailedWhenDatasetSyncFails()
    {
        await using var dbContext = CreateDbContext();
        var syncRun = CreateSyncRun(ProviderSyncDataset.Transactions);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var runner = new ProviderSyncRunner(dbContext, new FailingBankingSyncService(), new RecordingSyncProjectionRefresher(dbContext));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.Run(syncRun.Id, CancellationToken.None));

        var updatedRun = await dbContext.ProviderSyncRuns.SingleAsync();
        Assert.Equal(ProviderSyncStatus.Failed, updatedRun.Status);
        Assert.Contains("sync failed", updatedRun.Error);
        Assert.NotNull(updatedRun.CompletedAt);
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }

    private static ProviderSyncRun CreateSyncRun(string dataset)
    {
        return new ProviderSyncRun
        {
            TenantId = Guid.NewGuid(),
            Provider = ProviderSyncProvider.Fiskil,
            Dataset = dataset,
            Status = ProviderSyncStatus.Queued,
            EndUserId = "end-user-1",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class RecordingBankingSyncService : IFiskilBankingSyncService
    {
        public Guid? TransactionSyncRunId { get; private set; }

        public Task<SyncChangeSummary> SyncAccounts(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncChangeSummary.Empty);
        }

        public Task<SyncChangeSummary> SyncBalances(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncChangeSummary.Empty);
        }

        public Task<SyncChangeSummary> SyncTransactions(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            TransactionSyncRunId = syncRun.Id;
            return Task.FromResult(new SyncChangeSummary([Guid.NewGuid()], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, HasChanges: true));
        }
    }

    private sealed class FailingBankingSyncService : IFiskilBankingSyncService
    {
        public Task<SyncChangeSummary> SyncAccounts(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sync failed");
        }

        public Task<SyncChangeSummary> SyncBalances(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sync failed");
        }

        public Task<SyncChangeSummary> SyncTransactions(ProviderSyncRun syncRun, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sync failed");
        }
    }

    private sealed class RecordingSyncProjectionRefresher(FinyteDbContext dbContext) : ISyncProjectionRefresher
    {
        public Guid? SyncRunId { get; private set; }

        public async Task RefreshAfterSync(Guid syncRunId, SyncChangeSummary changeSummary, CancellationToken cancellationToken)
        {
            SyncRunId = syncRunId;
            var syncRun = await dbContext.ProviderSyncRuns.SingleAsync(x => x.Id == syncRunId, cancellationToken);
            syncRun.Status = ProviderSyncStatus.Succeeded;
            syncRun.CompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
