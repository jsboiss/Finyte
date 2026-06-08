using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilBankingSyncServiceTests
{
    [Fact]
    public async Task SyncTransactionsUpsertsTransactionsAndReturnsChangedDateRange()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var account = new Account
        {
            TenantId = tenantId,
            FiskilAccountId = "account-1",
            Name = "Everyday",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var syncRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        dbContext.Accounts.Add(account);
        dbContext.ProviderSyncRuns.Add(syncRun);
        dbContext.ProviderWebhookEvents.Add(new ProviderWebhookEvent
        {
            Provider = ProviderSyncProvider.Fiskil,
            MessageId = "message-1",
            EventType = "banking.transactions.sync.completed",
            TenantId = tenantId,
            SyncRunId = syncRun.Id,
            PayloadJson = """{"data":{"account_ids":["account-1"]}}""",
            ReceivedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var postedAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
        var client = new StubFiskilBankingClient
        {
            Transactions =
            [
                new FiskilTransactionData("transaction-1", "account-1", -25.50m, "AUD", "Coffee", "posted", postedAt, null, null, null, "Cafe", null, "{}")
            ]
        };
        var service = new FiskilBankingSyncService(dbContext, client);

        var summary = await service.SyncTransactions(syncRun, CancellationToken.None);

        var transaction = await dbContext.Transactions.SingleAsync();
        Assert.Equal("transaction-1", transaction.FiskilTransactionId);
        Assert.Equal(account.Id, transaction.AccountId);
        Assert.Equal(-25.50m, transaction.Amount);
        Assert.True(summary.HasChanges);
        Assert.Equal([account.Id], summary.AccountIds);
        Assert.Equal(postedAt, summary.MinChangedAt);
        Assert.Equal(postedAt, summary.MaxChangedAt);
    }

    [Fact]
    public async Task SyncBalancesUpdatesExistingAccounts()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var account = new Account
        {
            TenantId = tenantId,
            FiskilAccountId = "account-1",
            Name = "Everyday",
            CurrentBalance = 10,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var syncRun = CreateSyncRun(tenantId, ProviderSyncDataset.Balances);
        dbContext.Accounts.Add(account);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var client = new StubFiskilBankingClient
        {
            Balances =
            [
                new FiskilBalanceData("account-1", 123.45m, 120.00m, null, "AUD", DateTimeOffset.UtcNow, "{}")
            ]
        };
        var service = new FiskilBankingSyncService(dbContext, client);

        var summary = await service.SyncBalances(syncRun, CancellationToken.None);

        var updatedAccount = await dbContext.Accounts.SingleAsync();
        Assert.Equal(123.45m, updatedAccount.CurrentBalance);
        Assert.True(summary.HasChanges);
        Assert.Equal([account.Id], summary.AccountIds);
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
            Status = ProviderSyncStatus.Running,
            EndUserId = "end-user-1",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class StubFiskilBankingClient : IFiskilBankingClient
    {
        public IReadOnlyCollection<FiskilAccountData> Accounts { get; init; } = [];
        public IReadOnlyCollection<FiskilBalanceData> Balances { get; init; } = [];
        public IReadOnlyCollection<FiskilTransactionData> Transactions { get; init; } = [];

        public Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Accounts);
        }

        public Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Balances);
        }

        public Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Transactions);
        }
    }
}
