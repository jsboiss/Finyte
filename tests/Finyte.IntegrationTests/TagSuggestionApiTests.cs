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
        Assert.Equal(1200 + 3 * 5000 + 2 * 999, coverage.TotalMinorUnits); // The internal transfer is not spending.
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
        Assert.Equal(new AcceptTagSuggestionsResponse(2, 2), accepted);
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
        Assert.Equal(new AcceptTagSuggestionsResponse(0, 0), again);
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
        dbContext.InternalTransfers.Add(new InternalTransfer
        {
            TenantId = tenantId, DebitTransaction = debit, DebitTransactionId = debit.Id, CreditTransaction = credit, CreditTransactionId = credit.Id,
            DebitAccountId = account.Id, CreditAccountId = savings.Id, Amount = 500, Currency = "AUD", Status = "confirmed",
            DebitPostedAt = debit.PostedAt!.Value, CreditPostedAt = credit.PostedAt!.Value, ReviewedByUserId = "dev-user", UpdatedAt = now
        });
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
