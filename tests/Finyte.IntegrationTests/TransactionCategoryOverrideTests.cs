using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class TransactionCategoryOverrideTests
{
    [Fact]
    public async Task AnOverrideReplacesTheImportedCategoryAndClearingItRestoresTheOriginal()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        Assert.Equal("Department stores", await CategoryOf(client, seed.TransactionId));
        Assert.False(await HasOverride(client, seed.TransactionId));

        var set = await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = "Transport" });
        Assert.True(set.IsSuccessStatusCode, await set.Content.ReadAsStringAsync());
        Assert.Equal("Transport", await CategoryOf(client, seed.TransactionId));
        Assert.True(await HasOverride(client, seed.TransactionId));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var stored = await dbContext.Transactions.FindAsync(seed.TransactionId);
            Assert.Equal("Department stores", stored!.PrimaryCategory);
        }

        (await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal("Department stores", await CategoryOf(client, seed.TransactionId));
        Assert.False(await HasOverride(client, seed.TransactionId));
    }

    [Fact]
    public async Task ACorrectedTransactionMovesBetweenBudgets()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        Assert.Equal(45, await PreviewSpend(client, "Department stores"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Preview(client, "Transport")).StatusCode);

        (await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = "Transport" })).EnsureSuccessStatusCode();

        Assert.Equal(0, await PreviewSpend(client, "Department stores"));
        Assert.Equal(45, await PreviewSpend(client, "Transport"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankOverridesAreRejected(string category)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        var response = await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Department stores", await CategoryOf(client, seed.TransactionId));
    }

    [Fact]
    public async Task AnotherFamilysTransactionCannotBeRecategorised()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using var other = factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Dev-Organization", "org_category-isolation");
        (await other.PostAsJsonAsync("/api/auth/family", new { name = "Other family" })).EnsureSuccessStatusCode();

        var response = await other.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = "Transport" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Department stores", await CategoryOf(client, seed.TransactionId));
    }


    [Fact]
    public async Task AMerchantRuleCategorisesAnUploadedTransactionThatArrivedWithoutOne()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client, importedWithoutCategory: true);

        Assert.Equal("Uncategorised", await CategoryOf(client, seed.TransactionId));

        await CreateRule(client, "Kmart", "Transport");

        Assert.Equal("Transport", await CategoryOf(client, seed.TransactionId));
        Assert.False(await HasOverride(client, seed.TransactionId));
    }

    [Fact]
    public async Task AMerchantRuleReplacesTheCategoryTheBankSupplied()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);

        Assert.Equal("Department stores", await CategoryOf(client, seed.TransactionId));

        await CreateRule(client, "Kmart", "Transport");

        Assert.Equal("Transport", await CategoryOf(client, seed.TransactionId));
        Assert.False(await HasOverride(client, seed.TransactionId));
    }

    [Fact]
    public async Task ARuleMovesSpendBetweenBudgetsOnAConnectedAccount()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);

        Assert.Equal(45m, await PreviewSpend(client, "Department stores"));

        await CreateRule(client, "Kmart", "Transport");

        Assert.Equal(0m, await PreviewSpend(client, "Department stores"));
        Assert.Equal(45m, await PreviewSpend(client, "Transport"));
    }

    [Fact]
    public async Task AnOverrideStillBeatsAGuess()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client, importedWithoutCategory: true);
        await CreateRule(client, "Kmart", "Transport");

        (await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = "Home" })).EnsureSuccessStatusCode();
        Assert.Equal("Home", await CategoryOf(client, seed.TransactionId));

        (await client.PutAsJsonAsync($"/api/transactions/{seed.TransactionId}/category", new { category = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal("Transport", await CategoryOf(client, seed.TransactionId));
    }

    private static async Task CreateRule(HttpClient client, string merchantName, string category)
    {
        var tag = await client.PostAsJsonAsync("/api/tags", new { name = $"{category} tag", color = "#123456" });
        tag.EnsureSuccessStatusCode();
        var tagId = (await tag.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var rule = await client.PostAsJsonAsync("/api/merchant-tags", new { merchantName, tagId, category });
        Assert.True(rule.IsSuccessStatusCode, await rule.Content.ReadAsStringAsync());
    }

    private static async Task<string> CategoryOf(HttpClient client, Guid transactionId)
    {
        return (await ItemOf(client, transactionId)).GetProperty("category").GetString()!;
    }

    private static async Task<bool> HasOverride(HttpClient client, Guid transactionId)
    {
        return (await ItemOf(client, transactionId)).GetProperty("hasCategoryOverride").GetBoolean();
    }

    private static async Task<JsonElement> ItemOf(HttpClient client, Guid transactionId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        return page.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == transactionId);
    }

    private static async Task<HttpResponseMessage> Preview(HttpClient client, string category)
    {
        return await client.PostAsJsonAsync("/api/budgets/preview?date=2026-08-10", new
        {
            name = "Spending", limit = 100m, currency = "AUD", frequency = "monthly", anchorDate = "2026-08-01",
            matchMode = "selected", categories = new[] { category }, tagIds = Array.Empty<Guid>(),
            accountScope = "analytics", accountIds = Array.Empty<Guid>(), expectedVersion = (int?)null
        });
    }

    private static async Task<decimal> PreviewSpend(HttpClient client, string category)
    {
        var response = await Preview(client, category);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spent").GetDecimal();
    }

    private static async Task<SeedResult> Seed(FinyteApiFactory factory, HttpClient client, bool importedWithoutCategory = false)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Category family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{Guid.NewGuid():N}", CreatedAt = now };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}", StripePriceId = "price_test", Status = "active",
            CurrentPeriodEnd = now.AddDays(30), CreatedAt = now, UpdatedAt = now
        });
        var account = new Account { TenantId = tenantId, Name = "Everyday", Currency = "AUD", CurrentBalance = 500, BalanceAsOf = now.AddMinutes(1), CreatedAt = now };
        dbContext.Accounts.Add(account);
        var transaction = new Transaction
        {
            TenantId = tenantId, Account = account, FiskilTransactionId = Guid.NewGuid().ToString("N"),
            Amount = -45m, Currency = "AUD", Description = "Kmart 4821", MerchantName = "KMART",
            PrimaryCategory = importedWithoutCategory ? null : "Department stores", Status = "posted",
            PostedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), CreatedAt = now
        };
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync();
        return new SeedResult(tenantId, transaction.Id);
    }

    private sealed record SeedResult(Guid TenantId, Guid TransactionId);
}
