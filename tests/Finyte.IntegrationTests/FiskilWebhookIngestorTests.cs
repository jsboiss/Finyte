using Finyte.Api.ProviderSync;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilWebhookIngestorTests
{
    [Fact]
    public async Task IngestCreatesQueuedSyncRunForKnownConnection()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var tenantMemberId = Guid.NewGuid();
        dbContext.ProviderConnections.Add(new ProviderConnection
        {
            TenantId = tenantId,
            TenantMemberId = tenantMemberId,
            Provider = ProviderSyncProvider.Fiskil,
            EndUserId = "end-user-1",
            ConsentId = "consent-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var ingestor = new FiskilWebhookIngestor(dbContext);
        var request = new FiskilWebhookRequest(
            "message-1",
            DateTimeOffset.UtcNow,
            new FiskilWebhookData("banking.transactions.sync.completed", "consent-1", "end-user-1", "institution-1", ["account-1"]));

        var result = await ingestor.Ingest(request, "{}", CancellationToken.None);

        Assert.False(result.IsDuplicate);
        Assert.NotNull(result.SyncRunId);
        var syncRun = await dbContext.ProviderSyncRuns.SingleAsync();
        Assert.Equal(tenantId, syncRun.TenantId);
        Assert.Equal(ProviderSyncDataset.Transactions, syncRun.Dataset);
        Assert.Equal(ProviderSyncStatus.Queued, syncRun.Status);
    }

    [Fact]
    public async Task IngestDoesNotDuplicateWebhookMessages()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var tenantMemberId = Guid.NewGuid();
        dbContext.ProviderConnections.Add(new ProviderConnection
        {
            TenantId = tenantId,
            TenantMemberId = tenantMemberId,
            Provider = ProviderSyncProvider.Fiskil,
            EndUserId = "end-user-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var ingestor = new FiskilWebhookIngestor(dbContext);
        var request = new FiskilWebhookRequest(
            "message-1",
            DateTimeOffset.UtcNow,
            new FiskilWebhookData("banking.balances.sync.completed", ConsentId: null, "end-user-1", InstitutionId: null, AccountIds: null));

        await ingestor.Ingest(request, "{}", CancellationToken.None);
        var duplicate = await ingestor.Ingest(request, "{}", CancellationToken.None);

        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, await dbContext.ProviderWebhookEvents.CountAsync());
        Assert.Equal(1, await dbContext.ProviderSyncRuns.CountAsync());
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }
}
