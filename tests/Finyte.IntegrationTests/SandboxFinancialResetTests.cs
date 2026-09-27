using Finyte.Api.Development;
using Finyte.Core.Accounts;
using Finyte.Core.Budgets;
using Finyte.Core.ProviderSync;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.ProviderSync;
using Finyte.Data.Tagging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class SandboxFinancialResetTests
{
    [Fact]
    public async Task ResetRetainsBudgetScopeAndRulesWhichApplyToFreshlySyncedTransactions()
    {
        await using var dbContext = new FinyteDbContext(new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Name = "Test", ClerkOrganizationId = "test" };
        var account = new Account { TenantId = tenant.Id, Name = "Sandbox", FiskilAccountId = "account", ConsentId = "consent", CurrentBalance = 100, BalanceAsOf = DateTimeOffset.UtcNow };
        var oldAccount = new Account { TenantId = tenant.Id, Name = "Old import" };
        var otherAccount = new Account { TenantId = Guid.NewGuid(), Name = "Other household" };
        var tag = new TransactionTag { TenantId = tenant.Id, Name = "Utilities", Color = "#123456" };
        var rule = new MerchantTagRule { TenantId = tenant.Id, MerchantName = "Synergy", MerchantKey = "synergy", Tag = tag, TagId = tag.Id };
        var budget = new Budget { TenantId = tenant.Id, Name = "Utilities", Limit = 200, AccountScope = "selected",
            Accounts = [new BudgetAccount { AccountId = account.Id, Account = account }], Tags = [new BudgetTag { TagId = tag.Id, Tag = tag }] };
        var oldTransaction = new Transaction { TenantId = tenant.Id, AccountId = account.Id, FiskilTransactionId = "bank_tx_1", Amount = -155 };
        var otherTransaction = new Transaction { TenantId = otherAccount.TenantId, AccountId = otherAccount.Id, FiskilTransactionId = "other", Amount = -50 };
        dbContext.AddRange(tenant, account, oldAccount, otherAccount, tag, rule, budget, oldTransaction, otherTransaction);
        await dbContext.SaveChangesAsync();

        await SandboxFinancialReset.Clear(dbContext, tenant.Id, "consent", CancellationToken.None);

        Assert.Equal(otherTransaction.Id, (await dbContext.Transactions.SingleAsync()).Id);
        Assert.False(await dbContext.Accounts.AnyAsync(x => x.Id == oldAccount.Id));
        Assert.Equal(0, account.CurrentBalance);
        Assert.Null(account.BalanceAsOf);
        Assert.Equal(account.Id, (await dbContext.Set<BudgetAccount>().SingleAsync()).AccountId);
        Assert.Equal(200, (await dbContext.Budgets.SingleAsync()).Limit);
        Assert.Equal(tag.Id, (await dbContext.TransactionTags.SingleAsync()).Id);
        Assert.Equal(rule.Id, (await dbContext.MerchantTagRules.SingleAsync()).Id);

        var syncRun = new ProviderSyncRun { TenantId = tenant.Id, EndUserId = "sandbox", ConsentId = "consent", Dataset = ProviderSyncDataset.Transactions };
        var service = new FiskilBankingSyncService(dbContext, new BankingClient(), new TransactionTagService(dbContext));
        await service.SyncTransactions(syncRun, CancellationToken.None);
        var imported = await dbContext.Transactions.Include(x => x.TagAssignments).SingleAsync(x => x.TenantId == tenant.Id);
        Assert.NotEqual(oldTransaction.Id, imported.Id);
        Assert.Equal(tag.Id, Assert.Single(imported.TagAssignments).TagId);
        await service.SyncTransactions(syncRun, CancellationToken.None);
        Assert.Equal(1, await dbContext.Transactions.CountAsync(x => x.TenantId == tenant.Id));
    }

    private sealed class BankingClient : IFiskilBankingClient
    {
        public Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<FiskilTransactionData>>([new("bank_tx_1", "account", -155, "AUD", "Synergy", "POSTED", DateTimeOffset.UtcNow, null, "RENT_AND_UTILITIES", null, "Synergy", null, "{}")]);
        public Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
