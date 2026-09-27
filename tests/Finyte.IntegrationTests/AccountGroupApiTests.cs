using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class AccountGroupApiTests
{
    [Fact]
    public async Task GroupsAreCreatedListedRenamedAndDeletedWithValidation()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        var created = await client.PostAsJsonAsync("/api/account-groups", new { name = "Shared", accountIds = new[] { seed.Joint, seed.Solo } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/account-groups")).EnumerateArray());
        Assert.Equal("Shared", listed.GetProperty("name").GetString());
        Assert.Equal(2, listed.GetProperty("accountIds").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/account-groups", new { name = "shared", accountIds = new[] { seed.Joint } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account-groups", new { name = "Empty", accountIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account-groups", new { name = "Foreign", accountIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/account-groups", new { name = " ", accountIds = new[] { seed.Joint } })).StatusCode);

        var updated = await client.PutAsJsonAsync($"/api/account-groups/{group}", new { name = "Household", accountIds = new[] { seed.Joint } });
        updated.EnsureSuccessStatusCode();
        var renamed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/account-groups")).EnumerateArray());
        Assert.Equal("Household", renamed.GetProperty("name").GetString());
        Assert.Equal(1, renamed.GetProperty("accountIds").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/account-groups/{Guid.NewGuid()}", new { name = "X", accountIds = new[] { seed.Joint } })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/account-groups/{group}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/account-groups")).EnumerateArray());
    }

    [Fact]
    public async Task DashboardAndCashFlowUseAGroupOrSeveralAccountsAndCountOneCurrency()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var created = await client.PostAsJsonAsync("/api/account-groups", new { name = "Shared", accountIds = new[] { seed.Joint, seed.Solo, seed.Travel } });
        var group = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var shared = (await client.GetFromJsonAsync<OverviewResponse>($"/api/overview?groupId={group}"))!;
        Assert.Equal(3000, shared.CurrentMonthSpendMinorUnits);
        Assert.Equal("Shared", shared.Scope.Label);
        Assert.Equal("AUD", shared.Currency);
        Assert.Equal(1, shared.CurrencyScope!.ExcludedAccounts);

        var pair = (await client.GetFromJsonAsync<OverviewResponse>($"/api/overview?accountIds={seed.Joint}&accountIds={seed.Loan}"))!;
        Assert.Equal(5000, pair.CurrentMonthSpendMinorUnits);
        Assert.Equal("2 accounts", pair.Scope.Label);

        var one = (await client.GetFromJsonAsync<OverviewResponse>($"/api/overview?accountIds={seed.Joint}"))!;
        Assert.Equal(seed.Joint, one.Scope.AccountId);
        Assert.Null(one.Scope.AccountIds);

        var cashFlow = await client.GetFromJsonAsync<JsonElement>($"/api/cash-flow?from=2026-09-01&to=2026-09-08&groupId={group}");
        Assert.Equal(3000, cashFlow.GetProperty("dailyCashFlow").EnumerateArray().Sum(x => x.GetProperty("expenseMinorUnits").GetInt64()));
        Assert.Equal("AUD", cashFlow.GetProperty("currency").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/overview?groupId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/overview?accountId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/overview?accountIds={seed.Joint}&accountIds={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task TransactionsFilterBySeveralAccounts()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?accountIds={seed.Joint}&accountIds={seed.Solo}");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), x => Assert.Contains(x.GetProperty("accountId").GetGuid(), new[] { seed.Joint, seed.Solo }));
    }

    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task<SeedData> Seed(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Group family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = now };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active", CurrentPeriodEnd = now.AddDays(30), CreatedAt = now, UpdatedAt = now
        });
        var joint = new Account { TenantId = tenantId, Name = "A Joint", CreatedAt = now };
        var solo = new Account { TenantId = tenantId, Name = "B Solo", CreatedAt = now };
        var loan = new Account { TenantId = tenantId, Name = "C Loan", AccountTypeOverride = "home-loan", CreatedAt = now };
        var travel = new Account { TenantId = tenantId, Name = "D Travel", Currency = "USD", CreatedAt = now };
        dbContext.Accounts.AddRange(joint, solo, loan, travel);
        dbContext.Transactions.AddRange(Row(joint, -10m), Row(solo, -20m), Row(loan, -40m), Row(travel, -80m));
        await dbContext.SaveChangesAsync();
        return new SeedData(joint.Id, solo.Id, loan.Id, travel.Id);
    }

    private static Transaction Row(Account account, decimal amount) => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = amount,
        Currency = account.Currency, Status = "posted", PostedAt = new DateTimeOffset(2026, 9, 3, 2, 0, 0, TimeSpan.Zero), MerchantName = "Shop", Description = "Shop"
    };

    private sealed record SeedData(Guid Joint, Guid Solo, Guid Loan, Guid Travel);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
