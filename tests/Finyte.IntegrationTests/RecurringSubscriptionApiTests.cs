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

    [Fact]
    public async Task KindRoundTripsIsValidatedAndSplitsCosts()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var accountId = await Seed(factory, client, _ => []);
        var bill = await Create(client, Manual(accountId) with { Name = "Power", ExpectedAmount = 120, Kind = "bill" });
        Assert.Equal("bill", bill.Kind);
        var subscription = await Create(client, Manual(accountId) with { Name = "Streaming", ExpectedAmount = 12 });
        Assert.Equal("subscription", subscription.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Manual(accountId) with { Kind = "other" })).StatusCode);
        var updated = await client.PutAsJsonAsync($"{Url}/{bill.Id}", new UpdateRecurringRequest(bill.Name, bill.Cadence, bill.AnchorDate, bill.ExpectedAmount, bill.AmountMode, bill.State, bill.Version, "subscription"));
        updated.EnsureSuccessStatusCode();
        Assert.Equal("subscription", (await updated.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!.Kind);
        var back = await client.PutAsJsonAsync($"{Url}/{bill.Id}", new UpdateRecurringRequest(bill.Name, bill.Cadence, bill.AnchorDate, bill.ExpectedAmount, bill.AmountMode, bill.State, bill.Version + 1, "bill"));
        back.EnsureSuccessStatusCode();
        var cost = Assert.Single((await client.GetFromJsonAsync<RecurringSeriesList>(Url))!.Costs);
        Assert.Equal(12, cost.SubscriptionMonthlyEstimate);
        Assert.Equal(120, cost.BillMonthlyEstimate);
        Assert.Equal(132, cost.MonthlyEstimate);
    }

    [Fact]
    public async Task SeriesReportLastPaymentPriceChangesMissedPaymentsAndFilterByAccount()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var accountId = await Seed(factory, client, account => Enumerable.Range(4, 5).Select(month => Row(account, month, 5, -20m, "STREAMCO SYDNEY AUS")));
        var found = Assert.Single((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery"))!.Items);
        var history = found.Transactions.Where(x => x.OccurrenceDate.Month < 8)
            .Select(x => new RecurringHistoryInput(x.Snapshot.Id, x.OccurrenceDate, x.Snapshot.Fingerprint)).ToList();
        var series = await Create(client, new CreateRecurringRequest("Streaming", accountId, "AUD", "monthly", found.AnchorDate, 18, "fixed", [], history));
        Assert.Equal(20, series.LastPaidAmount);
        Assert.Equal(new DateOnly(2026, 7, 5), series.LastPaidDate);
        Assert.True(series.PriceChanged);
        Assert.Equal(new DateOnly(2026, 8, 5), series.MissedOccurrenceDate);
        Assert.Single((await client.GetFromJsonAsync<RecurringSeriesList>($"{Url}?accountId={accountId}"))!.Items);
        var other = (await client.GetFromJsonAsync<RecurringSeriesList>($"{Url}?accountId={Guid.NewGuid()}"))!;
        Assert.Empty(other.Items);
        Assert.Empty(other.Costs);
    }

    [Fact]
    public async Task UpcomingListsActiveChargesInRange()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var accountId = await Seed(factory, client, _ => []);
        var due = await Create(client, Manual(accountId) with { Name = "Streaming", AnchorDate = new(2026, 9, 10), Kind = "subscription" });
        var paused = await Create(client, Manual(accountId) with { Name = "Paused gym", AnchorDate = new(2026, 9, 11) });
        (await client.PutAsJsonAsync($"{Url}/{paused.Id}", new UpdateRecurringRequest(paused.Name, paused.Cadence, paused.AnchorDate, paused.ExpectedAmount, paused.AmountMode, "paused", paused.Version))).EnsureSuccessStatusCode();
        var week = (await client.GetFromJsonAsync<RecurringUpcomingPage>($"{Url}/upcoming?days=7"))!;
        var item = Assert.Single(week.Items);
        Assert.Equal(due.Id, item.SeriesId);
        Assert.Equal(new DateOnly(2026, 9, 10), item.Date);
        Assert.Equal("Everyday", item.AccountName);
        Assert.Equal(1, week.ActiveSeriesCount);
        Assert.Empty((await client.GetFromJsonAsync<RecurringUpcomingPage>($"{Url}/upcoming?days=1"))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<RecurringUpcomingPage>($"{Url}/upcoming?days=7&accountId={Guid.NewGuid()}"))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}/upcoming?days=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}/upcoming?days=32")).StatusCode);
    }

    private static CreateRecurringRequest Manual(Guid accountId) => new("Music", accountId, "AUD", "monthly", new(2026, 9, 10), 20, "fixed", [], []);

    private static async Task<RecurringSeriesResponse> Create(HttpClient client, CreateRecurringRequest request)
    {
        var response = await client.PostAsJsonAsync(Url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
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
