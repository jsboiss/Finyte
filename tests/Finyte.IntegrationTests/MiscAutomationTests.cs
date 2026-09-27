using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Core.Recurring;
using Finyte.Core.Transfers;
using Finyte.Data;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class MiscAutomationTests
{
    [Fact]
    public async Task ReclassificationIsIdempotentAndManualDecisionsSurviveIt()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<InternalTransferService>();
        Assert.Equal(2, await service.Reclassify(tenantId, default));
        Assert.Equal(0, await service.Reclassify(tenantId, default));
        Assert.Equal(2, await db.Transactions.CountAsync(x => x.InternalTransferAccountId != null));
        var debit = await db.Transactions.SingleAsync(x => x.Amount < 0);
        await service.Review(tenantId, new TransferDecisionRequest(debit.Id, "exclude", null), default);
        Assert.Equal(0, await service.Reclassify(tenantId, default));
        await db.Entry(debit).ReloadAsync();
        Assert.Null(debit.InternalTransferAccountId);
        Assert.Equal(InternalTransferDetector.Excluded, debit.InternalTransferSource);
        await service.Review(tenantId, new TransferDecisionRequest(debit.Id, "reset", null), default);
        await db.Entry(debit).ReloadAsync();
        Assert.NotNull(debit.InternalTransferAccountId);
        Assert.Equal(InternalTransferDetector.Detected, debit.InternalTransferSource);
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("foreign-tenant")]
    [InlineData("same-account")]
    [InlineData("ambiguous")]
    [InlineData("card")]
    public async Task RowsWithoutAUniqueCounterpartyStayUnclassified(string scenario)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var debit = await db.Transactions.SingleAsync(x => x.Amount < 0);
        var main = await db.Accounts.SingleAsync(x => x.Name == "Main");
        if (scenario == "unrelated") { debit.Description = "Transfer to savings"; }
        if (scenario == "foreign-tenant") { debit.TenantId = Guid.NewGuid(); }
        if (scenario == "same-account") { debit.Description = $"Transfer to xx{main.AccountNumber![^4..]}"; }
        if (scenario == "ambiguous") { db.Accounts.Add(new Account { TenantId = tenantId, Name = "Offset", AccountNumber = "77776486" }); }
        if (scenario == "card") { debit.Description = "Woolworths Card xx6486 Value Date: 01/09/2026"; }
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<InternalTransferService>().Reclassify(tenantId, default);
        await db.Entry(debit).ReloadAsync();
        Assert.Null(debit.InternalTransferAccountId);
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
    public async Task ImportClassifiesNewRowsAndCapturesTheFileAccountNumber()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var offset = new Account { TenantId = tenantId, Name = "Offset" };
        db.Accounts.Add(offset);
        await db.SaveChangesAsync();
        var file = "<OFX><BANKACCTFROM><BSB>062000<ACCTID>12345555</BANKACCTFROM><BANKTRANLIST>"
            + "<STMTTRN><DTPOSTED>20260906<TRNAMT>-12.34<FITID>auto-1<NAME>Coffee</STMTTRN>"
            + "<STMTTRN><DTPOSTED>20260906<TRNAMT>-50<FITID>auto-2<NAME>Transfer to xx6486</STMTTRN>"
            + "</BANKTRANLIST></OFX>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(file));
        await scope.ServiceProvider.GetRequiredService<Finyte.Data.Imports.TransactionFileImportService>().Import(tenantId, offset.Id, "test.ofx", stream, default);
        Assert.Equal("12345555", (await db.Accounts.SingleAsync(x => x.Id == offset.Id)).AccountNumber);
        var savings = await db.Accounts.SingleAsync(x => x.Name == "Savings");
        var imported = await db.Transactions.Where(x => x.AccountId == offset.Id).ToListAsync();
        Assert.Equal(savings.Id, imported.Single(x => x.Amount == -50).InternalTransferAccountId);
        Assert.Null(imported.Single(x => x.Amount == -12.34m).InternalTransferAccountId);
        Assert.Equal(2, (await db.TransactionFileImports.SingleAsync()).ImportedCount);
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
        var main = new Account { TenantId = tenantId, Name = "Main", AccountNumber = "12340449" };
        var savings = new Account { TenantId = tenantId, Name = "Savings", AccountNumber = "12346486" };
        db.Accounts.AddRange(main, savings);
        db.Transactions.AddRange(Row(tenantId, main.Id, -100, "Transfer to xx6486 CommBank app"), Row(tenantId, savings.Id, 100, "Transfer from xx0449"));
        await db.SaveChangesAsync();
        return tenantId;
    }

    private static Transaction Row(Guid tenantId, Guid accountId, decimal amount, string description) => new()
    {
        TenantId = tenantId, AccountId = accountId, Amount = amount, Currency = "AUD", Description = description,
        FiskilTransactionId = Guid.NewGuid().ToString(), Status = "posted", PostedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
    };
}
