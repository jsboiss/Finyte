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

public sealed class RecurringPaymentApiTests
{
    private static string Url => "/api/recurring-payments";

    [Fact]
    public async Task RankingPrecedesPaginationAndReadingSuggestionsNeverChangesDecisions()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId, [new("merchant", "Music")]);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            dbContext.Transactions.Add(Row(account, 9, 8, -20, "Unrelated shop"));
            await dbContext.SaveChangesAsync();
        }
        var first = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{Url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08&pageSize=1"))!;
        Assert.Equal(2, first.TotalCount);
        var ranked = Assert.Single(first.Items);
        Assert.Equal(seed.RenamedId, ranked.Snapshot.Id); // Older renamed payment outranks newer equal-price debit.
        Assert.Equal("medium", ranked.Ranking.Confidence);
        Assert.Equal("possible-name-change", ranked.Ranking.MatchKind);
        Assert.Equal(RecurringCandidateRanker.Version, ranked.Ranking.Version);
        var second = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{Url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08&pageSize=1&page=2"))!;
        Assert.Equal("low", Assert.Single(second.Items).Ranking.Confidence);
        var history = (await client.GetFromJsonAsync<RecurringReviewPage>($"{Url}/{series.Id}/history"))!;
        Assert.Empty(history.Items);
        var current = Assert.Single((await client.GetFromJsonAsync<RecurringSeriesList>(Url))!.Items);
        Assert.Equal(0, current.Version);
        Assert.Equal(20, current.ExpectedAmount);
        Assert.Single(current.Aliases);
    }

    [Fact]
    public async Task NarrowingDatesCannotHideCompetitionAndRejectionRemovesOnlyItsOwnSuggestion()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId, [new("merchant", "New Music Billing")]);
        Guid competingId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            var competing = Row(account, 9, 7, -24, "New Music Billing");
            competingId = competing.Id;
            dbContext.Transactions.Add(competing);
            await dbContext.SaveChangesAsync();
        }
        var narrowUrl = $"{Url}/{series.Id}/transactions?from=2026-09-05&to=2026-09-05&pageSize=1";
        var candidate = Assert.Single((await client.GetFromJsonAsync<RecurringCandidatePage>(narrowUrl))!.Items);
        Assert.Equal("ambiguous", candidate.Ranking.Confidence);
        Assert.Equal(1, candidate.Ranking.CompetingPaymentCount);
        var competingCandidate = await Candidate(client, series.Id, competingId);
        (await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(competingId,
            new(2026, 9, 5), "reject", series.Version, competingCandidate.Snapshot.Fingerprint, null))).EnsureSuccessStatusCode();
        candidate = Assert.Single((await client.GetFromJsonAsync<RecurringCandidatePage>(narrowUrl))!.Items);
        Assert.Equal("high", candidate.Ranking.Confidence);
        Assert.Equal(0, candidate.Ranking.CompetingPaymentCount);
        var rejected = await Candidate(client, series.Id, competingId);
        Assert.Equal("rejected", rejected.DecisionStatus);
        Assert.Equal("review-only", rejected.Ranking.MatchKind);
    }

    [Fact]
    public async Task OverlappingSeriesAreComparedBeforeEitherIsConfirmed()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var first = await Manual(client, seed.AccountId, [new("merchant", "New Music Billing")]);
        var second = await Manual(client, seed.AccountId, [new("merchant", "New Music Billing")]);
        var candidate = await Candidate(client, first.Id, seed.RenamedId);
        Assert.Equal("ambiguous", candidate.Ranking.Confidence);
        Assert.Equal(second.Id, Assert.Single(candidate.Ranking.CompetingSeriesIds));
        (await client.PutAsJsonAsync($"{Url}/{second.Id}", new UpdateRecurringRequest(second.Name, second.Cadence,
            second.AnchorDate, second.ExpectedAmount, second.AmountMode, "paused", second.Version))).EnsureSuccessStatusCode();
        candidate = await Candidate(client, first.Id, seed.RenamedId);
        Assert.Equal("high", candidate.Ranking.Confidence);
        Assert.Empty(candidate.Ranking.CompetingSeriesIds);
    }

    [Fact]
    public async Task DiscoveryUsesAnalyticsPreferencesEligibleDebitsAndReversibleStableDismissal()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var patterns = (await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-04-01&to=2026-08-31"))!;
        var pattern = Assert.Single(patterns.Items);
        Assert.Equal(seed.AccountId, pattern.AccountId);
        Assert.Equal(5, pattern.Transactions.Count);
        Assert.Equal("monthly", pattern.Cadence);
        (await client.PostAsJsonAsync($"{Url}/discovery/decisions", new RecurringDiscoveryDecisionRequest(pattern.Key, "dismiss"))).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-05-01&to=2026-08-31"))!.Items);
        var dismissed = (await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-05-01&to=2026-08-31&dismissed=true"))!;
        Assert.Equal(pattern.Key, Assert.Single(dismissed.Items).Key);
        Assert.Equal(4, dismissed.Items[0].Transactions.Count);
        (await client.PostAsJsonAsync($"{Url}/discovery/decisions", new RecurringDiscoveryDecisionRequest(pattern.Key, "reset"))).EnsureSuccessStatusCode();
        Assert.Single((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-05-01&to=2026-08-31"))!.Items);
    }

    [Fact]
    public async Task ExplicitHistoryAcceptanceDoesNotAutoLearnAliasesAndUnpaidDueDoesNotMeanCancellation()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var pattern = Assert.Single((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-04-01&to=2026-08-31"))!.Items);
        var history = pattern.Transactions.Select(x => new RecurringHistoryInput(x.Snapshot.Id, x.OccurrenceDate, x.Snapshot.Fingerprint)).ToList();
        var created = await client.PostAsJsonAsync(Url, new CreateRecurringRequest("Music", seed.AccountId, "AUD", pattern.Cadence, pattern.AnchorDate, 20, "variable", [], history));
        created.EnsureSuccessStatusCode();
        var series = (await created.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        Assert.Empty(series.Aliases);
        Assert.Equal("active", series.State);
        var future = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{Url}/{series.Id}/occurrences?from=2026-04-01&to=2026-10-31"))!;
        Assert.Equal(5, future.Items.Count(x => x.Status == "paid"));
        Assert.Equal("due", Assert.Single(future.Items, x => x.Date == new DateOnly(2026, 9, 5)).Status);
        var list = (await client.GetFromJsonAsync<RecurringSeriesList>($"{Url}?from=2026-04-01&to=2026-10-31"))!;
        Assert.Equal(240, Assert.Single(list.Costs).AnnualEstimate);
        Assert.Equal(1, list.Costs[0].VariableSeriesCount);
        Assert.Equal(new DateOnly(2026, 9, 5), Assert.Single(list.Items).NextDueDate);
        Assert.Empty((await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{Url}/discovery?from=2026-04-01&to=2026-08-31"))!.Items);
    }

    [Fact]
    public async Task AliasApprovalIsFieldAwareAndRenamesOrPricesNeverConfirmThemselves()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId, aliases: [new("description", "Music")]);
        var candidate = await Candidate(client, series.Id, seed.RenamedId);
        Assert.False(candidate.AliasMatch);
        Assert.True(candidate.AmountChanged);
        Assert.Null(candidate.DecisionStatus);
        var confirm = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, candidate.Snapshot.Fingerprint, "merchant"));
        confirm.EnsureSuccessStatusCode();
        series = (await confirm.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        Assert.Contains(series.Aliases, x => x.Field == "merchant" && x.Value == "New Music Billing");
        Assert.Equal(20, series.ExpectedAmount);
        Assert.Equal(24, Assert.Single((await client.GetFromJsonAsync<RecurringOccurrencePage>($"{Url}/{series.Id}/occurrences?from=2026-09-01&to=2026-09-30"))!.Items).PaidAmount);
        var remove = await client.DeleteAsync($"{Url}/{series.Id}/aliases/{series.Aliases.Single(x => x.Field == "merchant").Id}?expectedVersion={series.Version}");
        remove.EnsureSuccessStatusCode();
        var after = await Candidate(client, series.Id, seed.RenamedId);
        Assert.False(after.AliasMatch);
        Assert.Equal("confirmed", after.DecisionStatus);
    }

    [Fact]
    public async Task ProviderCorrectionsRemainVisibleOutsideSelectedHorizonAndCannotReuseStaleEvidence()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId);
        var candidate = await Candidate(client, series.Id, seed.RenamedId);
        var confirm = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", 0, candidate.Snapshot.Fingerprint, null));
        confirm.EnsureSuccessStatusCode();
        series = (await confirm.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            (await dbContext.Transactions.SingleAsync(x => x.Id == seed.RenamedId)).Description = "Corrected description";
            await dbContext.SaveChangesAsync();
        }
        var list = (await client.GetFromJsonAsync<RecurringSeriesList>($"{Url}?from=2026-10-01&to=2026-11-30"))!;
        Assert.Equal(1, Assert.Single(list.Items).NeedsReviewCount);
        Assert.Equal("needs-review", (await Candidate(client, series.Id, seed.RenamedId)).DecisionStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, candidate.Snapshot.Fingerprint, null))).StatusCode);
        var history = Assert.Single((await client.GetFromJsonAsync<RecurringReviewPage>($"{Url}/{series.Id}/history"))!.Items);
        Assert.NotEqual(history.Snapshot.Description, history.CurrentTransaction!.Description);
        Assert.Equal("needs-review", history.CurrentStatus);
    }

    [Fact]
    public async Task ScheduleEditsAllowExplicitReassignmentAndLeaveImmutablePriorEvidence()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId);
        var candidate = await Candidate(client, series.Id, seed.RenamedId);
        var confirm = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", 0, candidate.Snapshot.Fingerprint, null));
        confirm.EnsureSuccessStatusCode();
        series = (await confirm.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        var update = await client.PutAsJsonAsync($"{Url}/{series.Id}", new UpdateRecurringRequest("Music", "monthly", new(2026, 9, 6), 20, "fixed", "active", series.Version));
        update.EnsureSuccessStatusCode();
        series = (await update.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        Assert.Equal(1, series.NeedsReviewCount);
        var rebind = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 6), "confirm", series.Version, candidate.Snapshot.Fingerprint, null));
        rebind.EnsureSuccessStatusCode();
        var occurrences = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{Url}/{series.Id}/occurrences?from=2026-09-01&to=2026-09-30"))!;
        Assert.Equal("paid", Assert.Single(occurrences.Items).Status);
        Assert.Equal(new DateOnly(2026, 9, 6), occurrences.Items[0].Date);
        var history = (await client.GetFromJsonAsync<RecurringReviewPage>($"{Url}/{series.Id}/history"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Contains(history.Items, x => x.Action == "reassigned" && x.OccurrenceDate == new DateOnly(2026, 9, 5));
    }

    [Fact]
    public async Task RejectAndResetPersistAndLifecycleNeverErasesPaidHistoryOrMixesCurrencyCosts()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId);
        var candidate = await Candidate(client, series.Id, seed.RenamedId);
        var reject = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "reject", 0, candidate.Snapshot.Fingerprint, null));
        reject.EnsureSuccessStatusCode();
        series = (await reject.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        Assert.Equal("rejected", (await Candidate(client, series.Id, seed.RenamedId)).DecisionStatus);
        var reset = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "reset", series.Version, null, null));
        reset.EnsureSuccessStatusCode();
        series = (await reset.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        Assert.Null((await Candidate(client, series.Id, seed.RenamedId)).DecisionStatus);
        var confirm = await client.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, candidate.Snapshot.Fingerprint, null));
        confirm.EnsureSuccessStatusCode();
        series = (await confirm.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
        foreach (var state in new[] { "paused", "cancelled" })
        {
            var update = await client.PutAsJsonAsync($"{Url}/{series.Id}", new UpdateRecurringRequest("Music", "monthly", series.AnchorDate, 20, "fixed", state, series.Version));
            update.EnsureSuccessStatusCode();
            series = (await update.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
            Assert.Null(series.NextDueDate);
            var occurrences = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{Url}/{series.Id}/occurrences?from=2026-09-01&to=2026-10-31"))!;
            Assert.Equal("paid", occurrences.Items[0].Status);
            Assert.Equal(state, occurrences.Items[1].Status);
            Assert.Empty((await client.GetFromJsonAsync<RecurringSeriesList>(Url))!.Costs);
        }
    }

    [Fact]
    public async Task TenantBillingAndReferenceBoundariesApplyToEveryOperation()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var series = await Manual(client, seed.AccountId);
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-recurring");
        await Seed(factory, otherClient);
        Assert.Empty((await otherClient.GetFromJsonAsync<RecurringSeriesList>(Url))!.Items);
        foreach (var endpoint in new[] { "occurrences", "transactions", "history" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"{Url}/{series.Id}/{endpoint}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsJsonAsync($"{Url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", 0, "unknown", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await otherClient.PostAsJsonAsync(Url, Request(seed.AccountId))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            (await dbContext.BillingSubscriptions.SingleAsync(x => x.TenantId == seed.TenantId)).Status = "canceled";
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync($"{Url}/discovery")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(Url, Request(seed.AccountId))).StatusCode);
    }

    [Theory]
    [InlineData("discovery?from=0001-01-01")]
    [InlineData("discovery?to=9999-12-31")]
    [InlineData("discovery?from=2026-09-01&to=2026-08-01")]
    [InlineData("discovery?from=2020-01-01&to=2026-01-01")]
    [InlineData("discovery?page=0")]
    [InlineData("discovery?page=2147483647&pageSize=100")]
    [InlineData("discovery?page=broken")]
    [InlineData("discovery?from=not-date")]
    public async Task InvalidRangesAndBindingReturnBadRequest(string suffix)
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}/{suffix}")).StatusCode);
    }

    [Fact]
    public async Task InvalidBodyArraysAndUnicodeExpansionReturnBadRequest()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Request(seed.AccountId) with { Aliases = [null!] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Request(seed.AccountId) with { History = [null!] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Request(seed.AccountId) with { Aliases = [new("merchant", new string('ﬃ', 200))] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Request(seed.AccountId) with { ExpectedAmount = 1.001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, Request(seed.AccountId) with { Currency = "USD" })).StatusCode);
    }

    private static async Task<RecurringCandidateResponse> Candidate(HttpClient client, Guid seriesId, Guid transactionId) =>
        Assert.Single((await client.GetFromJsonAsync<RecurringCandidatePage>($"{Url}/{seriesId}/transactions?from=2026-09-01&to=2026-09-08"))!.Items, x => x.Snapshot.Id == transactionId);

    private static async Task<RecurringSeriesResponse> Manual(HttpClient client, Guid accountId, IReadOnlyList<RecurringAliasInput>? aliases = null)
    {
        var response = await client.PostAsJsonAsync(Url, Request(accountId) with { Aliases = aliases ?? [] });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
    }

    private static CreateRecurringRequest Request(Guid accountId) => new("Music", accountId, "AUD", "monthly", new(2026, 9, 5), 20, "fixed", [], []);
    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task<SeedData> Seed(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Recurring family" });
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
        var account = new Account { TenantId = tenantId, Name = "Card", CreatedAt = now };
        var excluded = new Account { TenantId = tenantId, Name = "Excluded loan", AccountTypeOverride = "home-loan", CreatedAt = now };
        dbContext.Accounts.AddRange(account, excluded);
        foreach (var month in Enumerable.Range(4, 5))
        {
            dbContext.Transactions.AddRange(Row(account, month, 5, -20, "Music"), Row(excluded, month, 5, -100, "Loan repayment"));
        }
        var renamed = Row(account, 9, 5, -24, "New Music Billing");
        var pending = Row(account, 9, 6, -99, "Pending");
        pending.Status = "pending";
        var undated = Row(account, 9, 6, -99, "Undated");
        undated.PostedAt = null;
        dbContext.Transactions.AddRange(renamed, pending, undated, Row(account, 9, 10, -99, "Future"), Row(account, 9, 6, 99, "Credit"));
        var debit = Row(account, 9, 7, -50, "Internal transfer");
        var credit = Row(excluded, 9, 7, 50, "Internal transfer");
        dbContext.Transactions.AddRange(debit, credit);
        dbContext.InternalTransfers.Add(new InternalTransfer
        {
            TenantId = tenantId, DebitTransaction = debit, DebitTransactionId = debit.Id, CreditTransaction = credit, CreditTransactionId = credit.Id,
            DebitAccountId = account.Id, CreditAccountId = excluded.Id, Amount = 50, Currency = "AUD", Status = "confirmed",
            DebitPostedAt = debit.PostedAt!.Value, CreditPostedAt = credit.PostedAt!.Value, ReviewedByUserId = "dev-user", UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return new SeedData(tenantId, account.Id, renamed.Id);
    }

    private static Transaction Row(Account account, int month, int day, decimal amount, string merchant) => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = amount,
        Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero), MerchantName = merchant, Description = merchant
    };
    private sealed record SeedData(Guid TenantId, Guid AccountId, Guid RenamedId);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
