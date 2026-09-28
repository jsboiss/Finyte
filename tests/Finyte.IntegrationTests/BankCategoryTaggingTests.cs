using System.Net.Http.Json;
using System.Text;
using Finyte.Core.Accounts;
using Finyte.Core.ProviderSync;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Imports;
using Finyte.Data.ProviderSync;
using Finyte.Data.Tagging;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class BankCategoryTaggingTests
{
    [Fact]
    public async Task ConnectedPaymentsGetTheirBankCategoryTagButImportedPaymentsDoNot()
    {
        await using var dbContext = CreateDbContext();
        var tenant = new Tenant { ClerkOrganizationId = "org_category", Name = "Category family" };
        var tenantId = tenant.Id;
        dbContext.Tenants.Add(tenant);
        var account = new Account { TenantId = tenantId, FiskilAccountId = "provider-account", Name = "Provider" };
        var imported = new Account { TenantId = tenantId, Name = "Imported" };
        dbContext.Accounts.AddRange(account, imported);
        await dbContext.SaveChangesAsync();
        var (service, client, tagService) = CreateSync(dbContext);
        client.Transactions = [Provider("one", "SHOPCO", "MERCHANDISE", "MERCHANDISE_DEPARTMENT_STORES")];

        await service.SyncTransactions(Run(tenantId), CancellationToken.None);
        var assignment = Assert.Single(await dbContext.TransactionTagAssignments.Include(x => x.Tag).ToListAsync());
        Assert.Equal("Shopping", assignment.Tag!.Name);
        Assert.Equal(TransactionTagSource.BankCategory, assignment.Source);
        Assert.Equal(MerchantKeywordCatalog.Starter("Shopping")!.Color, assignment.Tag.Color);
        Assert.False((await service.SyncTransactions(Run(tenantId), CancellationToken.None)).HasChanges);

        var importService = new TransactionFileImportService(dbContext, new ProjectionInvalidator(dbContext, TestCalendar.Tenants(dbContext)), tagService,
            new InternalTransferService(dbContext, new ProjectionInvalidator(dbContext, TestCalendar.Tenants(dbContext)), TestCalendar.Tenants(dbContext)), TestCalendar.Tenants(dbContext));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20260909<TRNAMT>-12.00<FITID>shop-two<NAME>SHOPCO</STMTTRN></BANKTRANLIST></OFX>"));
        await importService.Import(tenantId, imported.Id, "transactions.ofx", stream, CancellationToken.None);
        Assert.Empty((await dbContext.Transactions.Include(x => x.TagAssignments).SingleAsync(x => x.AccountId == imported.Id)).TagAssignments);
    }

    [Fact]
    public async Task ChosenTagsReplaceTheBankCategoryAndRemovedOnesStayRemoved()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var household = new TransactionTag { TenantId = tenantId, Name = "Household", Color = "#aaaaaa" };
        dbContext.Accounts.Add(new Account { TenantId = tenantId, FiskilAccountId = "provider-account", Name = "Provider" });
        dbContext.TransactionTags.Add(household);
        await dbContext.SaveChangesAsync();
        var (service, client, tagService) = CreateSync(dbContext);
        client.Transactions = [Provider("one", "SHOPCO", "MERCHANDISE", null), Provider("two", "CAFECO", "FOOD_AND_DRINK", null)];
        await service.SyncTransactions(Run(tenantId), CancellationToken.None);
        var shop = await dbContext.Transactions.Include(x => x.TagAssignments).Include(x => x.TagExclusions).SingleAsync(x => x.FiskilTransactionId == "one");
        var cafe = await dbContext.Transactions.Include(x => x.TagAssignments).Include(x => x.TagExclusions).SingleAsync(x => x.FiskilTransactionId == "two");

        tagService.SetTags(shop, new HashSet<Guid> { household.Id }, new HashSet<Guid>());
        tagService.SetTags(cafe, new HashSet<Guid>(), new HashSet<Guid>());
        await dbContext.SaveChangesAsync();
        await service.SyncTransactions(Run(tenantId), CancellationToken.None);

        var kept = Assert.Single(shop.TagAssignments);
        Assert.Equal(household.Id, kept.TagId);
        Assert.Equal(TransactionTagSource.Manual, kept.Source);
        Assert.Empty(cafe.TagAssignments);
    }

    [Fact]
    public async Task AllFromThisMerchantReplacesTheBankCategoryForEveryPaymentAndFutureOnes()
    {
        await using var factory = new FinyteApiFactory();
        using var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/family", new { name = "Category family" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<CurrentUser>())!.TenantId;
        Guid first, second, other, household, gifts;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = new Account { TenantId = tenantId, FiskilAccountId = "provider-account", Name = "Everyday" };
            var householdTag = new TransactionTag { TenantId = tenantId, Name = "Household", Color = "#aaaaaa" };
            var giftsTag = new TransactionTag { TenantId = tenantId, Name = "Gifts", Color = "#bbbbbb" };
            dbContext.Accounts.Add(account);
            dbContext.TransactionTags.AddRange(householdTag, giftsTag);
            var rows = new[] { ("SHOPCO SPRINGFIELD", 8), ("SHOPCO SPRINGFIELD", 15), ("CAFECO", 16) }
                .Select(x => Connected(tenantId, account.Id, x.Item1, x.Item2)).ToList();
            dbContext.Transactions.AddRange(rows);
            await dbContext.SaveChangesAsync();
            await new TransactionTagService(dbContext).ReconcileTenant(tenantId, CancellationToken.None);
            await dbContext.SaveChangesAsync();
            (first, second, other, household, gifts) = (rows[0].Id, rows[1].Id, rows[2].Id, householdTag.Id, giftsTag.Id);
        }

        (await http.PutAsJsonAsync($"/api/transactions/{second}/tags", new { tagIds = new[] { gifts } })).EnsureSuccessStatusCode();
        var merchant = await http.PutAsJsonAsync($"/api/transactions/{first}/tags/merchant", new { tagIds = new[] { household } });
        merchant.EnsureSuccessStatusCode();
        Assert.Equal("merchant-rule", Assert.Single((await merchant.Content.ReadFromJsonAsync<List<TagResponse>>())!).Source);

        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var tags = await db.TransactionTagAssignments.Include(x => x.Tag).ToListAsync();
        Assert.Equal(["Household"], tags.Where(x => x.TransactionId == first).Select(x => x.Tag!.Name));
        Assert.Equal(["Gifts", "Household"], tags.Where(x => x.TransactionId == second).Select(x => x.Tag!.Name).Order());
        Assert.Equal(["Shopping"], tags.Where(x => x.TransactionId == other).Select(x => x.Tag!.Name));
        var rule = Assert.Single(await db.MerchantTagRules.ToListAsync());
        Assert.Equal("SHOPCO SPRINGFIELD", rule.MerchantName);
        Assert.Equal(household, rule.TagId);

        (await http.PutAsJsonAsync($"/api/transactions/{first}/tags/merchant", new { tagIds = new[] { gifts } })).EnsureSuccessStatusCode();
        using var replaced = factory.Services.CreateScope();
        Assert.Equal(gifts, Assert.Single(await replaced.ServiceProvider.GetRequiredService<FinyteDbContext>().MerchantTagRules.ToListAsync()).TagId);
    }

    private static Transaction Connected(Guid tenantId, Guid accountId, string merchant, int day) => new()
    {
        TenantId = tenantId, AccountId = accountId, FiskilTransactionId = Guid.NewGuid().ToString("N"), MerchantName = merchant, Description = merchant,
        Amount = -20, PrimaryCategory = "MERCHANDISE", PostedAt = new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero), CreatedAt = DateTimeOffset.UtcNow
    };

    private static (FiskilBankingSyncService Service, StubFiskilBankingClient Client, TransactionTagService Tags) CreateSync(FinyteDbContext dbContext)
    {
        var client = new StubFiskilBankingClient();
        var tagService = new TransactionTagService(dbContext);
        return (new FiskilBankingSyncService(dbContext, client, tagService,
            new InternalTransferService(dbContext, new ProjectionInvalidator(dbContext, TestCalendar.Tenants(dbContext)), TestCalendar.Tenants(dbContext))), client, tagService);
    }

    private static ProviderSyncRun Run(Guid tenantId) => new() { TenantId = tenantId, EndUserId = "end-user" };

    private static FiskilTransactionData Provider(string id, string merchant, string primary, string? secondary) =>
        new(id, "provider-account", -12m, "AUD", merchant, "posted", new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero), null, primary, secondary, merchant, null, "{}");

    private static FinyteDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinyteDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed record CurrentUser(Guid TenantId);
    private sealed record TagResponse(Guid Id, string Name, string? Source);

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
