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

    [Fact]
    public async Task RevokedConnectionStaysRevokedAndIsNotSynced()
    {
        await using var dbContext = CreateDbContext();
        var connection = Connection("consent-1", ProviderConnectionStatus.Revoked);
        dbContext.ProviderConnections.Add(connection);
        await dbContext.SaveChangesAsync();
        var ingestor = new FiskilWebhookIngestor(dbContext);

        var withConsent = await ingestor.Ingest(Request("message-1", "banking.transactions.sync.completed", "consent-1"), "{}", CancellationToken.None);
        var withoutConsent = await ingestor.Ingest(Request("message-2", "banking.balances.sync.completed", null), "{}", CancellationToken.None);

        Assert.Null(withConsent.SyncRunId);
        Assert.Null(withoutConsent.SyncRunId);
        Assert.Equal(ProviderConnectionStatus.Revoked, (await dbContext.ProviderConnections.SingleAsync()).Status);
        Assert.Empty(await dbContext.ProviderSyncRuns.ToListAsync());
        Assert.Equal(2, await dbContext.ProviderWebhookEvents.CountAsync());
    }

    [Fact]
    public async Task NewConsentAfterARevokedOneIsActiveAndSynced()
    {
        await using var dbContext = CreateDbContext();
        dbContext.ProviderConnections.Add(Connection("consent-1", ProviderConnectionStatus.Revoked));
        await dbContext.SaveChangesAsync();
        var ingestor = new FiskilWebhookIngestor(dbContext);

        var result = await ingestor.Ingest(Request("message-1", "banking.accounts.sync.completed", "consent-2"), "{}", CancellationToken.None);

        Assert.NotNull(result.SyncRunId);
        var connections = await dbContext.ProviderConnections.ToDictionaryAsync(x => x.ConsentId!);
        Assert.Equal(ProviderConnectionStatus.Revoked, connections["consent-1"].Status);
        Assert.Equal(ProviderConnectionStatus.Active, connections["consent-2"].Status);
        Assert.Equal("consent-2", (await dbContext.ProviderSyncRuns.SingleAsync()).ConsentId);
    }

    [Fact]
    public async Task EventWithoutConsentPrefersAConnectionThatIsNotRevoked()
    {
        await using var dbContext = CreateDbContext();
        var revoked = Connection("consent-1", ProviderConnectionStatus.Revoked);
        var active = Connection("consent-2", ProviderConnectionStatus.Active);
        active.CreatedAt = revoked.CreatedAt.AddMinutes(1);
        dbContext.ProviderConnections.AddRange(revoked, active);
        await dbContext.SaveChangesAsync();
        var ingestor = new FiskilWebhookIngestor(dbContext);

        var result = await ingestor.Ingest(Request("message-1", "banking.balances.sync.completed", null), "{}", CancellationToken.None);

        Assert.NotNull(result.SyncRunId);
        Assert.Equal("consent-2", (await dbContext.ProviderSyncRuns.SingleAsync()).ConsentId);
        Assert.Equal(ProviderConnectionStatus.Revoked, (await dbContext.ProviderConnections.SingleAsync(x => x.Id == revoked.Id)).Status);
    }

    private static ProviderConnection Connection(string consentId, string status) => new()
    {
        TenantId = TenantId,
        TenantMemberId = Guid.NewGuid(),
        Provider = ProviderSyncProvider.Fiskil,
        EndUserId = "end-user-1",
        ConsentId = consentId,
        Status = status,
        CreatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static FiskilWebhookRequest Request(string messageId, string eventType, string? consentId) =>
        new(messageId, DateTimeOffset.UtcNow, new FiskilWebhookData(eventType, consentId, "end-user-1", "institution-1", ["account-1"]));

    private static readonly Guid TenantId = Guid.NewGuid();

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }
}
