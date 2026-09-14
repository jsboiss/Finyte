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
using Npgsql;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class RecurringPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task RealDatabasePreservesEvidenceAndSerializesCompetingReviews()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"recurring_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var scopedConnection = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
            await using var baseFactory = new FinyteApiFactory(scopedConnection);
            await using var factory = baseFactory.WithWebHostBuilder(x => x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new RecurringClock())));
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            var seed = await Seed(factory, client);
            const string url = "/api/recurring-payments";
            var discoveries = (await client.GetFromJsonAsync<RecurringDiscoveryPage>($"{url}/discovery?from=2026-04-01&to=2026-08-31"))!;
            var pattern = Assert.Single(discoveries.Items);
            Assert.Equal(5, pattern.Transactions.Count);
            Assert.Equal(18m, pattern.ExpectedAmount);
            Assert.Equal("Spending card", pattern.AccountName);
            var history = pattern.Transactions.Select(x => new RecurringHistoryInput(x.Snapshot.Id, x.OccurrenceDate, x.Snapshot.Fingerprint)).ToArray();
            var response = await client.PostAsJsonAsync(url, new CreateRecurringRequest("Music", seed.AccountId, "AUD", pattern.Cadence,
                pattern.AnchorDate, pattern.ExpectedAmount, "fixed", [new(pattern.AliasField, pattern.AliasValue)], history));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var series = (await response.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
            var occurrences = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{url}/{series.Id}/occurrences?from=2026-04-01&to=2026-09-30"))!;
            Assert.Equal(5, occurrences.Items.Count(x => x.Status == "paid"));

            var candidates = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08&pageSize=1"))!;
            Assert.Equal(3, candidates.TotalCount);
            var secondPage = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08&pageSize=1&page=2"))!;
            Assert.NotEqual(Assert.Single(candidates.Items).Snapshot.Id, Assert.Single(secondPage.Items).Snapshot.Id);
            var all = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08"))!;
            var renamed = Assert.Single(all.Items, x => x.Snapshot.Id == seed.RenamedId);
            Assert.False(renamed.AliasMatch);
            Assert.True(renamed.AmountChanged);
            Assert.Equal("possible-name-change", renamed.Ranking.MatchKind);
            Assert.Equal("medium", renamed.Ranking.Confidence);
            Assert.Equal(RecurringCandidateRanker.Version, renamed.Ranking.Version);
            var confirm = await client.PostAsJsonAsync($"{url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, renamed.Snapshot.Fingerprint, "merchant"));
            Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
            series = (await confirm.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
            Assert.Contains(series.Aliases, x => x.Value == "New Music Billing");
            Assert.Equal(18m, series.ExpectedAmount);
            var paid = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{url}/{series.Id}/occurrences?from=2026-09-01&to=2026-09-30"))!;
            Assert.Equal(24m, Assert.Single(paid.Items).PaidAmount);

            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                var row = await dbContext.Transactions.SingleAsync(x => x.Id == seed.RenamedId);
                row.Amount = -29m;
                row.MerchantName = "Corrected Music Billing";
                await dbContext.SaveChangesAsync();
            }
            var changed = (await client.GetFromJsonAsync<RecurringOccurrencePage>($"{url}/{series.Id}/occurrences?from=2026-09-01&to=2026-09-30"))!;
            Assert.Equal("needs-review", Assert.Single(changed.Items).Status);
            Assert.Null(Assert.Single(changed.Items).PaidAmount);
            var stale = await client.PostAsJsonAsync($"{url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, renamed.Snapshot.Fingerprint, null));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var audit = (await client.GetFromJsonAsync<RecurringReviewPage>($"{url}/{series.Id}/history"))!;
            var oldEvidence = Assert.Single(audit.Items, x => x.TransactionId == seed.RenamedId);
            Assert.Equal(-24m, oldEvidence.Snapshot.Amount);
            Assert.Equal(-29m, oldEvidence.CurrentTransaction!.Amount);
            Assert.Equal("needs-review", oldEvidence.CurrentStatus);

            var rejected = await client.PostAsJsonAsync($"{url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "reject", series.Version, oldEvidence.CurrentTransaction.Fingerprint, null));
            rejected.EnsureSuccessStatusCode();
            series = (await rejected.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
            var refreshed = (await client.GetFromJsonAsync<RecurringCandidatePage>($"{url}/{series.Id}/transactions?from=2026-09-01&to=2026-09-08"))!;
            Assert.Equal("rejected", Assert.Single(refreshed.Items, x => x.Snapshot.Id == seed.RenamedId).DecisionStatus);

            var competing = await CreateManual(client, seed.AccountId);
            var contests = await Task.WhenAll(
                client.PostAsJsonAsync($"{url}/{series.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", series.Version, oldEvidence.CurrentTransaction.Fingerprint, null)),
                client.PostAsJsonAsync($"{url}/{competing.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "confirm", competing.Version, oldEvidence.CurrentTransaction.Fingerprint, null)));
            Assert.Single(contests, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(contests, x => x.StatusCode == HttpStatusCode.Conflict);
            var winner = (await contests.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
            var edits = await Task.WhenAll(
                client.PutAsJsonAsync($"{url}/{winner.Id}", new UpdateRecurringRequest("Music one", winner.Cadence, winner.AnchorDate, 29, "fixed", "active", winner.Version)),
                client.PutAsJsonAsync($"{url}/{winner.Id}", new UpdateRecurringRequest("Music two", winner.Cadence, winner.AnchorDate, 30, "fixed", "active", winner.Version)));
            Assert.Single(edits, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(edits, x => x.StatusCode == HttpStatusCode.Conflict);
            winner = (await edits.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;

            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                var row = await dbContext.Transactions.SingleAsync(x => x.Id == seed.RenamedId);
                dbContext.Transactions.Remove(row);
                await dbContext.SaveChangesAsync();
            }
            var deletedHistory = (await client.GetFromJsonAsync<RecurringReviewPage>($"{url}/{winner.Id}/history"))!;
            Assert.Contains(deletedHistory.Items, x => x.TransactionId == seed.RenamedId && x.CurrentTransaction == null && x.CurrentStatus == "needs-review");
            var release = await client.PostAsJsonAsync($"{url}/{winner.Id}/decisions", new RecurringDecisionRequest(seed.RenamedId, new(2026, 9, 5), "reset", winner.Version, null, null));
            release.EnsureSuccessStatusCode();
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                Assert.False(await dbContext.RecurringPaymentDecisions.AnyAsync(x => x.TransactionId == seed.RenamedId && x.Status == "confirmed"));
                Assert.True(await dbContext.RecurringPaymentReviews.AnyAsync(x => x.TransactionId == seed.RenamedId && x.Action == "reset"));
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<RecurringSeriesResponse> CreateManual(HttpClient client, Guid accountId)
    {
        var response = await client.PostAsJsonAsync("/api/recurring-payments", new CreateRecurringRequest("Other plan", accountId, "AUD", "monthly", new(2026, 9, 5), 24, "fixed", [], []));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecurringSeriesResponse>())!;
    }

    private static async Task<SeedData> Seed(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/family", new { name = "Recurring test family" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = DateTimeOffset.UtcNow };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription { TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId, StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active", CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30) });
        var account = new Account { TenantId = tenantId, Name = "Provider card", CustomName = "Spending card", Currency = "AUD" };
        dbContext.Accounts.Add(account);
        foreach (var month in Enumerable.Range(4, 5))
        {
            dbContext.Transactions.Add(Payment(tenantId, account, month, 5, month < 7 ? -15 : -18, "Music"));
        }
        var renamed = Payment(tenantId, account, 9, 5, -24, "New Music Billing");
        dbContext.Transactions.AddRange(renamed, Payment(tenantId, account, 9, 6, -12, "One-off purchase"), Payment(tenantId, account, 9, 7, -8, "Another purchase"));
        await dbContext.SaveChangesAsync();
        return new SeedData(account.Id, renamed.Id);
    }

    private static Transaction Payment(Guid tenantId, Account account, int month, int day, decimal amount, string merchant) => new()
    {
        TenantId = tenantId, Account = account, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = amount, Currency = "AUD", Status = "posted",
        PostedAt = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero), MerchantName = merchant, Description = merchant
    };

    private sealed record SeedData(Guid AccountId, Guid RenamedId);
    private sealed class RecurringClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
