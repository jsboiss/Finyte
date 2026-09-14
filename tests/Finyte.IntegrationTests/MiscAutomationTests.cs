using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Core.Recurring;
using Finyte.Data;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class MiscAutomationTests
{
    [Fact]
    public async Task BoundedReconciliationKeepsHistoricalDecisionsAndDetectsEdgeCompetitors()
    {
        await using var factory = new FinyteApiFactory();
        await CheckBoundedReconciliation(factory);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlBoundedReconciliationKeepsHistoricalDecisionsAndDetectsEdgeCompetitors()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"bounded_transfers_{Guid.NewGuid():N}";
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var factory = new FinyteApiFactory(new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            await CheckBoundedReconciliation(factory);
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task CheckBoundedReconciliation(FinyteApiFactory factory)
    {
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AutomaticTransferService>();
        Assert.Equal(1, await service.Reconcile(tenantId, default));
        var historical = await db.InternalTransfers.SingleAsync();
        var accounts = await db.Accounts.ToListAsync();
        var debit = Row(tenantId, accounts[0].Id, -200, "Transfer to savings");
        debit.PostedAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var credit = Row(tenantId, accounts[1].Id, 200, "Transfer from main");
        credit.PostedAt = debit.PostedAt.Value.AddDays(3);
        var competitor = Row(tenantId, accounts[0].Id, -200, "Card purchase");
        competitor.PostedAt = debit.PostedAt.Value.AddDays(6);
        db.Transactions.AddRange(debit, credit, competitor);
        await db.SaveChangesAsync();
        Assert.Equal(0, await service.Reconcile(tenantId, default, new(2026, 10, 10), new(2026, 10, 10)));
        Assert.Equal("confirmed", historical.Status);
        Assert.Single(await db.InternalTransfers.ToListAsync());
        competitor.Amount = -300;
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.Reconcile(tenantId, default, new(2026, 10, 10), new(2026, 10, 10)));
        // A newly imported competitor must invalidate a pair even when only its
        // partner is in the changed window's three-day focus.
        competitor.Amount = -200;
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.Reconcile(tenantId, default, new(2026, 10, 16), new(2026, 10, 16)));
        Assert.Equal("confirmed", historical.Status);
        Assert.Equal("needs-review", (await db.InternalTransfers.SingleAsync(x => x.DebitTransactionId == debit.Id)).Status);
    }

    [Fact]
    public async Task AutomaticMatchIsIdempotentAndUndoSurvivesReconciliation()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AutomaticTransferService>();
        Assert.Equal(1, await service.Reconcile(tenantId, default));
        Assert.Equal(0, await service.Reconcile(tenantId, default));
        Assert.Single(await db.ValidConfirmedTransfers(tenantId).ToListAsync());
        var pair = await db.InternalTransfers.SingleAsync();
        await scope.ServiceProvider.GetRequiredService<InternalTransferService>().Review(tenantId, "dev-user",
            new(pair.DebitTransactionId, pair.CreditTransactionId, "reset", pair.Amount, pair.Currency,
                pair.DebitAccountId, pair.CreditAccountId, pair.DebitPostedAt, pair.CreditPostedAt), default);
        Assert.Equal(0, await service.Reconcile(tenantId, default));
        Assert.Empty(await db.ValidConfirmedTransfers(tenantId).ToListAsync());
        Assert.Equal("dismissed", pair.Status);
    }

    [Fact]
    public async Task NewCompetingPaymentReleasesAutomaticExclusionForReview()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AutomaticTransferService>();
        Assert.Equal(1, await service.Reconcile(tenantId, default));
        var credit = await db.Transactions.SingleAsync(x => x.Amount > 0);
        db.Transactions.Add(Row(tenantId, credit.AccountId, 100, "Unrelated refund"));
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.Reconcile(tenantId, default));
        Assert.Empty(await db.ValidConfirmedTransfers(tenantId).ToListAsync());
        Assert.Equal("needs-review", (await db.InternalTransfers.SingleAsync()).Status);
        var review = await scope.ServiceProvider.GetRequiredService<InternalTransferService>().GetReview(tenantId, "needs-review", new(2026, 9, 1), new(2026, 9, 30), 1, default);
        Assert.Single(review.Items);
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("pending")]
    [InlineData("currency")]
    [InlineData("same-account")]
    [InlineData("foreign-tenant")]
    [InlineData("ambiguous")]
    public async Task UncertainPairsStayUnclassified(string scenario)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var credit = await db.Transactions.SingleAsync(x => x.Amount > 0);
        if (scenario == "unrelated") { credit.Description = "Refund"; }
        if (scenario == "pending") { credit.Status = "pending"; }
        if (scenario == "foreign-tenant") { credit.TenantId = Guid.NewGuid(); }
        if (scenario == "currency") { credit.Currency = "USD"; }
        if (scenario == "same-account") { credit.AccountId = await db.Transactions.Where(x => x.Amount < 0).Select(x => x.AccountId).SingleAsync(); }
        if (scenario == "ambiguous") { db.Transactions.Add(Row(tenantId, credit.AccountId, 100, "Transfer from main")); }
        await db.SaveChangesAsync();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AutomaticTransferService>().Reconcile(tenantId, default));
        Assert.Empty(await db.InternalTransfers.ToListAsync());
    }

    [Fact]
    public async Task DiscoveryCanSearchExcludedAccountAndFiltersBeforePaging()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.Name == "Savings");
            account.IncludeInAnalyticsOverride = false;
            accountId = account.Id;
            foreach (var month in new[] { 4, 5, 6 })
            {
                var row = Row(tenantId, accountId, -25, "Music subscription");
                row.PostedAt = new DateTimeOffset(2026, month, 5, 0, 0, 0, TimeSpan.Zero);
                row.MerchantName = "Music subscription";
                db.Transactions.Add(row);
            }
            await db.SaveChangesAsync();
        }
        const string url = "/api/recurring-payments/discovery?from=2026-04-01&to=2026-06-30";
        Assert.Empty((await client.GetFromJsonAsync<RecurringDiscoveryPage>(url))!.Items);
        var result = await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{url}&accountId={accountId}&search=music&cadence=monthly&pageSize=1");
        Assert.Equal(accountId, Assert.Single(result!.Items).AccountId);
        Assert.Equal(1, result.TotalCount);
        Assert.Empty((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{url}&accountId={accountId}&search=unknown"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{url}&accountId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ImportRunsAutomaticMatchingWithoutOpeningTransferReview()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var accountId = await db.Accounts.Where(x => x.Name == "Main").Select(x => x.Id).SingleAsync();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<OFX><BANKTRANLIST><STMTTRN><DTPOSTED>20260906<TRNAMT>-12.34<FITID>auto-test<NAME>Coffee</STMTTRN></BANKTRANLIST></OFX>"));
        await scope.ServiceProvider.GetRequiredService<Finyte.Data.Imports.TransactionFileImportService>().Import(tenantId, accountId, "test.ofx", stream, default);
        Assert.Single(await db.ValidConfirmedTransfers(tenantId).ToListAsync());
        Assert.Equal(1, (await db.TransactionFileImports.SingleAsync()).ImportedCount);
    }

    private static async Task<Guid> Seed(FinyteApiFactory factory, HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/family", new { name = "Automation test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = DateTimeOffset.UtcNow };
        db.BillingCustomers.Add(customer);
        db.BillingSubscriptions.Add(new BillingSubscription { TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "test", Status = "active", CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30) });
        var main = new Account { TenantId = tenantId, Name = "Main" };
        var savings = new Account { TenantId = tenantId, Name = "Savings" };
        db.Accounts.AddRange(main, savings);
        db.Transactions.AddRange(Row(tenantId, main.Id, -100, "Transfer to savings"), Row(tenantId, savings.Id, 100, "Transfer from main"));
        await db.SaveChangesAsync();
        return tenantId;
    }

    private static Transaction Row(Guid tenantId, Guid accountId, decimal amount, string description) => new()
    {
        TenantId = tenantId, AccountId = accountId, Amount = amount, Currency = "AUD", Description = description,
        FiskilTransactionId = Guid.NewGuid().ToString(), Status = "posted", PostedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
    };
}
