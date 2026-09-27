using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Finyte.Data.Tagging;
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
        var service = new FiskilBankingSyncService(dbContext, client, new TransactionTagService(dbContext));

        var summary = await service.SyncTransactions(syncRun, CancellationToken.None);

        var transaction = await dbContext.Transactions.SingleAsync();
        Assert.Equal("transaction-1", transaction.FiskilTransactionId);
        Assert.Equal(account.Id, transaction.AccountId);
        Assert.Equal(-25.50m, transaction.Amount);
        Assert.True(summary.HasChanges);
        Assert.Equal([account.Id], summary.AccountIds);
        Assert.Equal(postedAt, summary.MinChangedAt);
        Assert.Equal(postedAt, summary.MaxChangedAt);
        client.Transactions = [client.Transactions.Single() with { PostedAt = postedAt.AddMonths(2) }];
        var correction = await service.SyncTransactions(syncRun, CancellationToken.None);
        Assert.Equal(postedAt, correction.MinChangedAt);
        Assert.Equal(postedAt.AddMonths(2), correction.MaxChangedAt);
        client.Transactions = [client.Transactions.Single() with { PostedAt = null, Status = "pending" }];
        var unposted = await service.SyncTransactions(syncRun, CancellationToken.None);
        Assert.Equal(postedAt.AddMonths(2), unposted.MinChangedAt);
        Assert.Equal(postedAt.AddMonths(2), unposted.MaxChangedAt);
    }

    [Fact]
    public async Task SyncStoresAndUpdatesMerchantCodeCategoryConfidenceAndPaymentType()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var account = new Account { TenantId = tenantId, FiskilAccountId = "account-1", Name = "Everyday", CreatedAt = DateTimeOffset.UtcNow };
        var syncRun = CreateSyncRun(tenantId, ProviderSyncDataset.Transactions);
        dbContext.Accounts.Add(account);
        dbContext.ProviderSyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync();
        var postedAt = new DateTimeOffset(2026, 9, 26, 23, 43, 0, TimeSpan.Zero);
        var client = new StubFiskilBankingClient
        {
            Transactions =
            [
                new FiskilTransactionData("bank_tx_1", "account-1", -155m, "AUD", "PAYMENT TO SYNERGY", "POSTED", postedAt, postedAt,
                    "RENT_AND_UTILITIES", "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", "Synergy", null, "{}", "4900", "MEDIUM", "PAYMENT")
            ]
        };
        var service = new FiskilBankingSyncService(dbContext, client, new TransactionTagService(dbContext));

        await service.SyncTransactions(syncRun, CancellationToken.None);
        var stored = await dbContext.Transactions.SingleAsync();
        Assert.Equal(("4900", "MEDIUM", "PAYMENT"), (stored.MerchantCategoryCode, stored.CategoryConfidence, stored.PaymentType));

        client.Transactions = [client.Transactions.Single() with { CategoryConfidence = "VERY_HIGH", PaymentType = "DIRECT_DEBIT" }];
        Assert.True((await service.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
        stored = await dbContext.Transactions.SingleAsync();
        Assert.Equal(("VERY_HIGH", "DIRECT_DEBIT"), (stored.CategoryConfidence, stored.PaymentType));
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
        var service = new FiskilBankingSyncService(dbContext, client, new TransactionTagService(dbContext));

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
        public IReadOnlyCollection<FiskilTransactionData> Transactions { get; set; } = [];

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
