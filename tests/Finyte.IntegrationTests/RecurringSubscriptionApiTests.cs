using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Core.Recurring;
using Finyte.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringSubscriptionApiTests
{
    private static string Url => "/api/recurring-payments";

    [Fact]
    public async Task DiscoveryMarksEarlyAndEndedPatternsAndCanHideEnded()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var accountId = await Seed(factory, client, account => [
            Row(account, 8, 7, -9.99m, "STREAMCO SYDNEY AUS Card xx1234 Value Date: 05/08/2026"),
            Row(account, 9, 7, -9.99m, "STREAMCO SYDNEY AUS Card xx1234 Value Date: 05/09/2026"),
            Row(account, 1, 10, -30m, "Direct Debit 111111 GYMCO 222222"),
            Row(account, 2, 10, -30m, "Direct Debit 111111 GYMCO 222222"),
            Row(account, 3, 10, -30m, "Direct Debit 111111 GYMCO 222222")
        ]);
        var all = (await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery"))!;
        var early = Assert.Single(all.Items, x => x.Name == "STREAMCO SYDNEY AUS");
        Assert.True(early.IsEarly);
        Assert.Equal("subscription", early.SuggestedKind);
        var ended = Assert.Single(all.Items, x => x.AliasValue == "direct debit 111111 gymco 222222");
        Assert.True(ended.IsEnded);
        Assert.Equal("bill", ended.SuggestedKind);
        Assert.Equal(ended.Key, all.Items[^1].Key); // Ended patterns sort last.
        var current = (await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?hideEnded=true"))!;
        Assert.DoesNotContain(current.Items, x => x.IsEnded);
        Assert.Contains(current.Items, x => x.Key == early.Key);
        Assert.NotEqual(accountId, Guid.Empty);
    }

    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task<Guid> Seed(WebApplicationFactory<Program> factory, HttpClient client, Func<Account, IEnumerable<Transaction>> rows)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Subscription family" });
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
        var account = new Account { TenantId = tenantId, Name = "Everyday", CreatedAt = now };
        dbContext.Accounts.Add(account);
        dbContext.Transactions.AddRange(rows(account));
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    private static Transaction Row(Account account, int month, int day, decimal amount, string text) => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = amount,
        Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero), MerchantName = text, Description = text
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
