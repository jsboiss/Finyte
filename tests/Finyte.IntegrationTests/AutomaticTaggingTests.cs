using System.Text;
using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Imports;
using Finyte.Data.ProviderSync;
using Finyte.Data.Tagging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class AutomaticTaggingTests
{
    [Theory]
    [InlineData("COLES 4568 ASCOT AU", null, "Coles", true)]
    [InlineData("Colesworth", null, "Coles", false)]
    [InlineData(null, "Café & Co Brisbane", "Café Co", true)]
    [InlineData("Other Merchant", "Coles", "Coles", false)]
    [InlineData("Coffee Sydney", null, "Coffee Brisbane", false)]
    [InlineData("Any merchant", null, "!!!", false)]
    [InlineData(null, null, "Coles", false)]
    public void MatchingUsesWholeLeadingWordsAndDoesNotGuessAwayMerchantWords(string? merchant, string? description, string rule, bool expected)
    {
        Assert.Equal(expected, MerchantTagMatcher.Matches(merchant, description, MerchantTagMatcher.Normalize(rule)));
    }

    [Fact]
    public async Task ProviderAndOfxUseIdenticalRulesAndRepeatedSyncIsIdempotent()
    {
        await using var dbContext = CreateDbContext();
        var tenant = new Tenant { ClerkOrganizationId = "org_tagging", Name = "Tagging family" };
        var providerAccount = new Account { TenantId = tenant.Id, FiskilAccountId = "provider-account", Name = "Provider" };
        var importAccount = new Account { TenantId = tenant.Id, Name = "Imported" };
        var tag = new TransactionTag { TenantId = tenant.Id, Name = "Coffee", Color = "#aaaaaa" };
        var foreignTag = new TransactionTag { TenantId = Guid.NewGuid(), Name = "Private tag", Color = "#aaaaaa" };
        var rule = CreateRule(tenant.Id, tag.Id, "Coffee");
        dbContext.Tenants.Add(tenant);
        dbContext.Accounts.AddRange(providerAccount, importAccount);
        dbContext.TransactionTags.AddRange(tag, foreignTag);
        dbContext.MerchantTagRules.AddRange(rule, CreateRule(foreignTag.TenantId, foreignTag.Id, "Coffee"));
        await dbContext.SaveChangesAsync();
        var tagService = new TransactionTagService(dbContext);
        var client = new StubFiskilBankingClient { Transactions = [ProviderTransaction("Coffee Brisbane")] };
        var syncService = new FiskilBankingSyncService(dbContext, client, tagService);
        var syncRun = new ProviderSyncRun { TenantId = tenant.Id, EndUserId = "end-user" };

        Assert.True((await syncService.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
        Assert.False((await syncService.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
        var importService = new TransactionFileImportService(dbContext, new ProjectionInvalidator(dbContext, TestCalendar.Tenants(dbContext)), tagService, TestCalendar.Tenants(dbContext));
        const string content = "<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20260908<TRNAMT>-4.50<FITID>coffee-one<NAME>Coffee Brisbane</STMTTRN></BANKTRANLIST></OFX>";
        using var firstStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await importService.Import(tenant.Id, importAccount.Id, "transactions.ofx", firstStream, CancellationToken.None);
        using var repeatStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var repeat = await importService.Import(tenant.Id, importAccount.Id, "transactions.ofx", repeatStream, CancellationToken.None);

        Assert.Equal(1, repeat.SkippedCount);
        var assignments = await dbContext.TransactionTagAssignments.ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, x =>
        {
            Assert.Equal(tag.Id, x.TagId);
            Assert.Equal(TransactionTagSource.MerchantRule, x.Source);
            Assert.Equal(rule.Id, x.MerchantRuleId);
        });

        // A skipped reimport never overrides the transaction's manual exclusions.
        var imported = await dbContext.Transactions.SingleAsync(x => x.AccountId == importAccount.Id);
        tagService.SetTags(imported, new HashSet<Guid>(), new HashSet<Guid>());
        await dbContext.SaveChangesAsync();
        using var removedStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await importService.Import(tenant.Id, importAccount.Id, "transactions.ofx", removedStream, CancellationToken.None);
        Assert.Empty(imported.TagAssignments);
        Assert.Single(imported.TagExclusions);
    }

    [Fact]
    public async Task ProviderReconcilesMerchantChangesAndPreservesManualTagsAndRemovals()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var account = new Account { TenantId = tenantId, FiskilAccountId = "provider-account", Name = "Provider" };
        var coffee = new TransactionTag { TenantId = tenantId, Name = "Coffee", Color = "#aaaaaa" };
        var groceries = new TransactionTag { TenantId = tenantId, Name = "Groceries", Color = "#aaaaaa" };
        var personal = new TransactionTag { TenantId = tenantId, Name = "Personal", Color = "#aaaaaa" };
        dbContext.Accounts.Add(account);
        dbContext.TransactionTags.AddRange(coffee, groceries, personal);
        dbContext.MerchantTagRules.AddRange(CreateRule(tenantId, coffee.Id, "Coffee"), CreateRule(tenantId, groceries.Id, "Coles"));
        await dbContext.SaveChangesAsync();
        var tagService = new TransactionTagService(dbContext);
        var client = new StubFiskilBankingClient { Transactions = [ProviderTransaction("Coffee")] };
        var service = new FiskilBankingSyncService(dbContext, client, tagService);
        var syncRun = new ProviderSyncRun { TenantId = tenantId, EndUserId = "end-user" };
        await service.SyncTransactions(syncRun, CancellationToken.None);
        var transaction = await dbContext.Transactions.SingleAsync();
        tagService.SetTags(transaction, new HashSet<Guid> { personal.Id }, new HashSet<Guid>());
        await dbContext.SaveChangesAsync();
        Assert.False((await service.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
        Assert.Equal(personal.Id, Assert.Single(transaction.TagAssignments).TagId);

        client.Transactions = [ProviderTransaction("Coles")];
        Assert.True((await service.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
        Assert.Equal(2, transaction.TagAssignments.Count);
        Assert.Contains(transaction.TagAssignments, x => x.TagId == personal.Id && x.Source == TransactionTagSource.Manual);
        Assert.Contains(transaction.TagAssignments, x => x.TagId == groceries.Id && x.Source == TransactionTagSource.MerchantRule);

        client.Transactions = [ProviderTransaction("Coffee")];
        await service.SyncTransactions(syncRun, CancellationToken.None);
        Assert.Equal(personal.Id, Assert.Single(transaction.TagAssignments).TagId);
        tagService.ClearExclusions(transaction);
        await dbContext.SaveChangesAsync();
        var taggingOnly = await service.SyncTransactions(syncRun, CancellationToken.None);
        Assert.True(taggingOnly.HasChanges);
        Assert.Equal([account.Id], taggingOnly.AccountIds);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), taggingOnly.MinChangedAt);
        Assert.False((await service.SyncTransactions(syncRun, CancellationToken.None)).HasChanges);
    }

    [Fact]
    public async Task ReadingAndReconcilingLegacyRulesPreservesTheirSavedMatchingWords()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var tag = new TransactionTag { TenantId = tenantId, Name = "Groceries", Color = "#aaaaaa" };
        var rule = CreateRule(tenantId, tag.Id, "Coles Melbourne");
        rule.MerchantKey = "coles";
        dbContext.TransactionTags.Add(tag);
        dbContext.MerchantTagRules.Add(rule);
        await dbContext.SaveChangesAsync();
        var service = new TransactionTagService(dbContext);
        var rules = await service.GetRules(tenantId, CancellationToken.None);
        Assert.False(dbContext.ChangeTracker.HasChanges());
        var transaction = new Transaction { TenantId = tenantId, FiskilTransactionId = "legacy-test", MerchantName = "Coles Brisbane" };
        Assert.True(service.Reconcile(transaction, rules));
        Assert.Single(transaction.TagAssignments);
        Assert.Equal("coles", rule.MerchantKey);
        // An explicit edit opts into the new matching words and reconciles automatic assignments.
        rule.MerchantKey = MerchantTagMatcher.Normalize(rule.MerchantName);
        Assert.True(service.Reconcile(transaction, rules));
        Assert.Empty(transaction.TagAssignments);
    }

    private static FinyteDbContext CreateDbContext()
    {
        return new FinyteDbContext(new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    }

    private static MerchantTagRule CreateRule(Guid tenantId, Guid tagId, string name)
    {
        return new MerchantTagRule { TenantId = tenantId, TagId = tagId, MerchantName = name, MerchantKey = MerchantTagMatcher.Normalize(name) };
    }

    private static FiskilTransactionData ProviderTransaction(string merchant)
    {
        return new FiskilTransactionData("provider-transaction", "provider-account", -4.5m, "AUD", merchant, "posted",
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), null, null, null, merchant, null, "{}");
    }

    private sealed class StubFiskilBankingClient : IFiskilBankingClient
    {
        public IReadOnlyCollection<FiskilTransactionData> Transactions { get; set; } = [];
        public Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<FiskilAccountData>>([]);
        public Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<FiskilBalanceData>>([]);
        public Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken)
            => Task.FromResult(Transactions);
    }
}
