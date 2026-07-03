using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class SyncProjectionRefresherTests
{
    [Fact]
    public async Task RefreshAfterSyncWithNoChangesDoesNotInvalidateProjections()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var syncRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var invalidator = new RecordingProjectionInvalidator();
        var refresher = new SyncProjectionRefresher(dbContext, invalidator);

        await refresher.RefreshAfterSync(syncRun.Id, SyncChangeSummary.Empty, CancellationToken.None);

        var updatedRun = await dbContext.ProviderSyncRuns.SingleAsync();
        Assert.NotNull(updatedRun.ProjectionRefreshedAt);
        Assert.Empty(invalidator.TransactionChanges);
        Assert.Empty(invalidator.BalanceChanges);
        Assert.Empty(invalidator.TenantChanges);
    }

    [Fact]
    public async Task RefreshAfterSyncWaitsForPendingTenantSyncsThenRefreshesCoalescedChanges()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var firstRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        var secondRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        dbContext.ProviderSyncRuns.AddRange(firstRun, secondRun);
        await dbContext.SaveChangesAsync();
        var invalidator = new RecordingProjectionInvalidator();
        var refresher = new SyncProjectionRefresher(dbContext, invalidator);

        await refresher.RefreshAfterSync(
            firstRun.Id,
            new SyncChangeSummary([accountId], new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), HasChanges: true),
            CancellationToken.None);

        Assert.Empty(invalidator.TransactionChanges);
        Assert.Null((await dbContext.ProviderSyncRuns.SingleAsync(x => x.Id == firstRun.Id)).ProjectionRefreshedAt);

        await refresher.RefreshAfterSync(
            secondRun.Id,
            new SyncChangeSummary([accountId], new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero), HasChanges: true),
            CancellationToken.None);

        Assert.Collection(invalidator.TransactionChanges,
            x =>
            {
                Assert.Equal(tenantId, x.TenantId);
                Assert.Equal(accountId, x.AccountId);
                Assert.Equal(new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero), x.PostedAt);
            });
        Assert.All(await dbContext.ProviderSyncRuns.ToListAsync(), x => Assert.NotNull(x.ProjectionRefreshedAt));
    }

    [Fact]
    public async Task RefreshAfterSyncMarksTenantStaleForBroadChanges()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var accountIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();
        var syncRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var invalidator = new RecordingProjectionInvalidator();
        var refresher = new SyncProjectionRefresher(dbContext, invalidator);

        await refresher.RefreshAfterSync(
            syncRun.Id,
            new SyncChangeSummary(accountIds, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero), HasChanges: true),
            CancellationToken.None);

        Assert.Collection(invalidator.TenantChanges,
            x =>
            {
                Assert.Equal(tenantId, x.TenantId);
                Assert.Contains("provider sync", x.Reason);
            });
        Assert.Empty(invalidator.TransactionChanges);
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }

    private static ProviderSyncRun CreateSyncRun(Guid tenantId, string dataset)
    {
        return new ProviderSyncRun
        {
            TenantId = tenantId,
            Provider = ProviderSyncProvider.Fiskil,
            Dataset = dataset,
            Status = ProviderSyncStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class RecordingProjectionInvalidator : IProjectionInvalidator
    {
        public List<(Guid TenantId, Guid AccountId)> BalanceChanges { get; } = [];
        public List<(Guid TenantId, Guid AccountId, DateTimeOffset? PostedAt)> TransactionChanges { get; } = [];
        public List<(Guid TenantId, string Reason)> TenantChanges { get; } = [];

        public Task AccountBalanceChanged(Guid tenantId, Guid accountId, CancellationToken cancellationToken)
        {
            BalanceChanges.Add((tenantId, accountId));
            return Task.CompletedTask;
        }

        public Task TransactionChanged(Guid tenantId, Guid accountId, DateTimeOffset? postedAt, CancellationToken cancellationToken)
        {
            TransactionChanges.Add((tenantId, accountId, postedAt));
            return Task.CompletedTask;
        }

        public Task OverviewRequested(Guid tenantId, Guid? accountId, string monthKey, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task TenantProjectionDataChanged(Guid tenantId, string reason, CancellationToken cancellationToken)
        {
            TenantChanges.Add((tenantId, reason));
            return Task.CompletedTask;
        }
    }
}
