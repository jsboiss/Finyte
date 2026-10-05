using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.Tagging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class TagSuggestionApiTests
{
    private static string Url => "/api/tag-suggestions";

    [Fact]
    public async Task SuggestionsGroupUntaggedSpendingAndAcceptingCreatesRulesThatTagThePast()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);

        var suggestions = (await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!;
        var coverage = Assert.Single(suggestions.Coverage);
        Assert.Equal("AUD", coverage.Currency);
        Assert.Equal(1200, coverage.TaggedMinorUnits);
        Assert.Equal(1200 + 3 * 5000 + 2 * 999, coverage.TotalMinorUnits);
        var groceries = Assert.Single(suggestions.Groups);
        Assert.Equal("Groceries", groceries.TagName);
        Assert.Null(groceries.TagId);
        var coles = Assert.Single(groceries.Merchants);
        Assert.Equal("COLES", coles.RuleMerchantName);
        Assert.Equal(3, coles.TransactionCount);
        Assert.Equal(15000, coles.SpendMinorUnits);
        Assert.Equal(2, coles.Examples.Count);
        Assert.Equal("Keyword: coles", coles.Reason);
        var streamco = Assert.Single(suggestions.NeedsTag);
        Assert.Equal("STREAMCO SYDNEY AUS", streamco.RuleMerchantName);
        Assert.DoesNotContain(suggestions.Groups.SelectMany(x => x.Merchants).Concat(suggestions.NeedsTag), x => x.RuleMerchantName.Contains("CAFE"));

        var accept = new AcceptTagSuggestionsRequest([new("COLES", null, "Groceries"), new("STREAMCO SYDNEY AUS", null, "Subscriptions")]);
        var accepted = await (await client.PostAsJsonAsync($"{Url}/accept", accept)).Content.ReadFromJsonAsync<AcceptTagSuggestionsResponse>();
        Assert.Equal(new AcceptTagSuggestionsResponse(2, 2, 0), accepted);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tagged = await dbContext.Transactions.Include(x => x.TagAssignments).ThenInclude(x => x.Tag)
                .Where(x => x.Description!.StartsWith("COLES") || x.Description!.StartsWith("STREAMCO")).ToListAsync();
            Assert.Equal(5, tagged.Count);
            Assert.All(tagged, x => Assert.Contains(x.TagAssignments, y => y.Source == TransactionTagSource.MerchantRule));
            Assert.Equal("#bbf7d0", (await dbContext.TransactionTags.SingleAsync(x => x.Name == "Groceries")).Color);
        }
        var after = (await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!;
        Assert.Empty(after.Groups);
        Assert.Empty(after.NeedsTag);
        Assert.Equal(after.Coverage[0].TotalMinorUnits, after.Coverage[0].TaggedMinorUnits);

        var again = await (await client.PostAsJsonAsync($"{Url}/accept", accept)).Content.ReadFromJsonAsync<AcceptTagSuggestionsResponse>();
        Assert.Equal(new AcceptTagSuggestionsResponse(0, 0, 0), again);
    }

    [Fact]
    public async Task AcceptValidatesItemsAndTagsAndSuggestionsNeedBilling()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/accept", new AcceptTagSuggestionsRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest(Enumerable.Range(0, 201).Select(x => new AcceptTagSuggestionItem($"SHOP {x}", null, "Shopping")).ToList()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest([new("COLES", Guid.NewGuid(), "Groceries")]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest([new("!!!", null, "Groceries")]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest([new("COLES", Guid.NewGuid(), null)]))).StatusCode);

        await using var unpaidFactory = WithClock(new FinyteApiFactory());
        using var unpaidClient = unpaidFactory.CreateClient();
        await Seed(unpaidFactory, unpaidClient, billing: false);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await unpaidClient.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task TransfersAreSuggestedPerRecipientRatherThanAsOneBroadRule()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Name == "Everyday");
            dbContext.Transactions.AddRange(Row(account, 8, 10, -40m, "Transfer to xx3333 CommBank app Jamie"), Row(account, 8, 11, -60m, "Transfer to xx4444 CommBank app Sam"));
            await dbContext.SaveChangesAsync();
        }
        var transfers = Assert.Single((await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!.Groups, x => x.TagName == "Transfers");
        Assert.Equal(["Transfer to xx4444 CommBank app Sam", "Transfer to xx3333 CommBank app Jamie"], transfers.Merchants.Select(x => x.RuleMerchantName));
    }

    [Fact]
    public async Task BankCategoriesComeBeforeMerchantKeywords()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Name == "Everyday");
            foreach (var day in new[] { 4, 18 })
            {
                var power = Row(account, 8, day, -120m, "PAYMENT TO SYNERGY RETAIL 123");
                power.MerchantName = "Synergy";
                power.PrimaryCategory = "RENT_AND_UTILITIES";
                power.SecondaryCategory = "RENT_AND_UTILITIES_GAS_AND_ELECTRICITY";
                var fuel = Row(account, 8, day, -60m, "COLES EXPRESS 999 SPRINGFIELD");
                fuel.PrimaryCategory = "TRANSPORTATION";
                fuel.SecondaryCategory = "TRANSPORTATION_GAS";
                dbContext.Transactions.AddRange(power, fuel);
            }
            await dbContext.SaveChangesAsync();
        }
        var suggestions = (await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!;
        var bills = Assert.Single(suggestions.Groups, x => x.TagName == "Bills and utilities");
        var synergy = Assert.Single(bills.Merchants);
        Assert.Equal("Synergy", synergy.RuleMerchantName);
        Assert.Equal("Bank category: RENT_AND_UTILITIES_GAS_AND_ELECTRICITY", synergy.Reason);
        Assert.Contains(Assert.Single(suggestions.Groups, x => x.TagName == "Fuel").Merchants, x => x.RuleMerchantName.StartsWith("COLES EXPRESS"));
    }

    [Fact]
    public async Task PendingPaymentsAreSuggestedButNotCountedInCoverage()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);
        var before = (await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Name == "Everyday");
            var pending = Row(account, 9, 7, -49m, "GROCERCO RICHMOND AUS");
            pending.Status = "pending";
            pending.PostedAt = null;
            pending.CreatedAt = new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero);
            pending.PrimaryCategory = "FOOD_AND_DRINK";
            pending.SecondaryCategory = "FOOD_AND_DRINK_GROCERIES";
            dbContext.Transactions.Add(pending);
            await dbContext.SaveChangesAsync();
        }
        var after = (await client.GetFromJsonAsync<TagSuggestionsResponse>(Url))!;
        Assert.Contains(Assert.Single(after.Groups, x => x.TagName == "Groceries").Merchants, x => x.RuleMerchantName == "GROCERCO RICHMOND AUS");
        Assert.Equal(before.Coverage, after.Coverage);
    }

    [Fact]
    public async Task ApplyingOnceTagsExistingPaymentsWithoutARule()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);

        var accepted = await (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest([new("STREAMCO SYDNEY AUS", null, "Subscriptions", "once")]))).Content.ReadFromJsonAsync<AcceptTagSuggestionsResponse>();
        Assert.Equal(new AcceptTagSuggestionsResponse(1, 0, 2), accepted);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        Assert.Empty(dbContext.MerchantTagRules);
        var tagged = await dbContext.Transactions.Include(x => x.TagAssignments).Where(x => x.Description!.StartsWith("STREAMCO")).ToListAsync();
        Assert.All(tagged, x => Assert.Equal(TransactionTagSource.Manual, Assert.Single(x.TagAssignments).Source));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Url}/accept",
            new AcceptTagSuggestionsRequest([new("STREAMCO SYDNEY AUS", null, "Subscriptions", "sometimes")]))).StatusCode);
    }

    [Fact]
    public async Task TransactionsCarryACleanedMerchantNameForRules()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client, billing: true);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/transactions?search=STREAMCO");
        var names = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("ruleMerchantName").GetString()).Distinct().ToList();
        Assert.Equal(["STREAMCO SYDNEY AUS"], names);
    }

    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task Seed(WebApplicationFactory<Program> factory, HttpClient client, bool billing)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Tagging family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (billing)
        {
            var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = now };
            dbContext.BillingCustomers.Add(customer);
            dbContext.BillingSubscriptions.Add(new BillingSubscription
            {
                TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
                StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active", CurrentPeriodEnd = now.AddDays(30), CreatedAt = now, UpdatedAt = now
            });
        }
        var account = new Account { TenantId = tenantId, Name = "Everyday", CreatedAt = now };
        var savings = new Account { TenantId = tenantId, Name = "Savings", CreatedAt = now };
        dbContext.Accounts.AddRange(account, savings);
        dbContext.Transactions.AddRange(
            Row(account, 8, 1, -50m, "COLES 1111 SPRINGFIELD QLD AUS Card xx1234 Value Date: 30/07/2026"),
            Row(account, 8, 15, -50m, "COLES 1111 SPRINGFIELD QLD AUS Card xx1234 Value Date: 13/08/2026"),
            Row(account, 8, 20, -50m, "COLES 2222 SHELBYVILLE QLD AUS Card xx1234 Value Date: 18/08/2026"),
            Row(account, 7, 7, -9.99m, "STREAMCO SYDNEY AUS Card xx1234 AUD 9.99 Value Date: 05/07/2026"),
            Row(account, 8, 7, -9.99m, "STREAMCO SYDNEY AUS Card xx1234 AUD 9.99 Value Date: 05/08/2026"));
        var treat = Row(account, 8, 2, -12m, "CAFE ALREADY TAGGED");
        var tag = new TransactionTag { TenantId = tenantId, Name = "Treats", Color = "#fecdd3", CreatedAt = now };
        dbContext.Transactions.Add(treat);
        dbContext.TransactionTags.Add(tag);
        dbContext.TransactionTagAssignments.Add(new TransactionTagAssignment { Transaction = treat, TransactionId = treat.Id, Tag = tag, TagId = tag.Id, Source = TransactionTagSource.Manual, CreatedAt = now });
        var debit = Row(account, 8, 3, -500m, "Transfer to xx9999 NetBank Savings");
        var credit = Row(savings, 8, 3, 500m, "Transfer from xx1111 NetBank Savings");
        dbContext.Transactions.AddRange(debit, credit);
        debit.InternalTransferAccountId = savings.Id;
        debit.InternalTransferSource = "manual";
        credit.InternalTransferAccountId = account.Id;
        credit.InternalTransferSource = "manual";
        await dbContext.SaveChangesAsync();
    }

    private static Transaction Row(Account account, int month, int day, decimal amount, string text) => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = amount,
        Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, month, day, 2, 0, 0, TimeSpan.Zero), MerchantName = text, Description = text
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
