using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Core.PayCycles;
using Finyte.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class PayCycleTests
{
    [PostgreSqlFact]
    public async Task PostgreSqlMigrationsClassificationsTotalsPagingAndConcurrentEdits()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"paycycle_test_{Guid.NewGuid():N}";
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
            await using var factory = WithClock(baseFactory);
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            var seed = await Seed(factory, client);
            var profile = await Create(client, seed);
            var firstPage = (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profile.Id}/breakdown?pageSize=2"))!;
            Assert.Equal(610, firstPage.Totals.NetMovement);
            Assert.Equal(250, firstPage.Totals.NetSavingsTransfers);
            Assert.Equal(8, firstPage.Transactions.TotalCount);
            Assert.Equal(2, firstPage.Transactions.Items.Count);
            Assert.Equal("Groceries", Assert.Single(firstPage.SpendingCategories).Name);
            var savings = (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profile.Id}/breakdown?kind=savings-out"))!;
            Assert.Equal(-300, Assert.Single(savings.Transactions.Items).Amount);
            Assert.Equal(610, savings.Totals.NetMovement);
            var secondPage = (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profile.Id}/breakdown?page=2&pageSize=2"))!;
            Assert.Empty(firstPage.Transactions.Items.Select(x => x.Id).Intersect(secondPage.Transactions.Items.Select(x => x.Id)));
            var updates = await Task.WhenAll(
                client.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(seed, "weekly")),
                client.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(seed, "monthly")));
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict);
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.SecondId);
                dbContext.Accounts.Remove(account);
                await dbContext.SaveChangesAsync();
            }
            var missing = await Breakdown(client, profile.Id);
            Assert.Contains(seed.SecondId, missing.MissingAccountIds);
            Assert.Single(missing.Accounts);
            Assert.DoesNotContain(missing.Accounts, x => x.Id == seed.SavingsId);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("monthly", "2026-01-31", "2026-02-28", "2026-02-28", "2026-03-31")]
    [InlineData("monthly", "2026-01-31", "2026-03-30", "2026-02-28", "2026-03-31")]
    [InlineData("monthly", "2026-01-31", "2026-03-31", "2026-03-31", "2026-04-30")]
    [InlineData("monthly", "2024-01-31", "2024-02-29", "2024-02-29", "2024-03-31")]
    [InlineData("weekly", "2026-09-08", "2026-09-07", "2026-09-01", "2026-09-08")]
    [InlineData("fortnightly", "2026-09-08", "2026-09-09", "2026-09-08", "2026-09-22")]
    [InlineData("fortnightly", "2026-09-08", "2026-08-24", "2026-08-11", "2026-08-25")]
    [InlineData("monthly", "2026-01-31", "1900-01-01", "1899-12-31", "1900-01-31")]
    [InlineData("monthly", "2026-01-31", "9998-12-31", "9998-12-31", "9999-01-31")]
    public void CalendarHasStableAnchorsAcrossShortMonthsAndDatesBeforeTheAnchor(string frequency, string anchor, string date, string from, string to)
    {
        var period = PayCycleCalendar.Resolve(frequency, DateOnly.Parse(anchor), DateOnly.Parse(date));
        Assert.Equal(DateOnly.Parse(from), period.From);
        Assert.Equal(DateOnly.Parse(to), period.ToExclusive);
    }

    [Fact]
    public async Task BreakdownReconcilesRecordedActivityWithoutGuessingSalaryOrTransfers()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        var data = await Breakdown(client, profile.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), data.From);
        Assert.Equal(new DateOnly(2026, 9, 14), data.To);
        Assert.Equal(new DateOnly(2026, 9, 8), data.ObservedThrough);
        Assert.Equal("current", data.PeriodStatus);
        Assert.Equal("Australia/Sydney", data.DateBasis);
        Assert.Equal(1000, data.Totals.ExternalCredits);
        Assert.Equal(100, data.Totals.Spending);
        Assert.Equal(300, data.Totals.SavingsTransfersOut);
        Assert.Equal(50, data.Totals.SavingsTransfersIn);
        Assert.Equal(250, data.Totals.NetSavingsTransfers);
        Assert.Equal(40, data.Totals.OtherTransfersOut);
        Assert.Equal(0, data.Totals.TransfersIn);
        Assert.Equal(0, data.Totals.WithinScopeTransfers);
        Assert.Equal(610, data.Totals.NetMovement);
        Assert.Equal(0, data.Totals.ExpectedIncomeDifference);
        Assert.Equal(8, data.Totals.TransactionCount);
        Assert.Equal(data.Totals.NetMovement, data.Transactions.Items.Sum(x => x.Amount));
        Assert.Equal("Groceries", Assert.Single(data.SpendingCategories).Name);
        Assert.Equal(1, data.UndatedTransactionCount);
        Assert.Equal(1, data.UnpostedTransactionCount);
        Assert.Equal(1, data.OtherCurrencyTransactionCount);
        Assert.Contains(data.Accounts, x => x.Id == seed.MainId && !x.IncludeInAnalytics && x.Name == "My everyday");
        Assert.Single(data.SavingsAccounts);
        Assert.Empty(data.MissingAccountIds);
    }

    [Fact]
    public async Task SplitSettlementCountsEachLegInItsOwnCycleAndFutureHasNoActuals()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed, "weekly");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var debit = await dbContext.Transactions.SingleAsync(x => x.AccountId == seed.MainId && x.InternalTransferAccountId == seed.SecondId);
            var credit = await dbContext.Transactions.SingleAsync(x => x.AccountId == seed.SecondId && x.InternalTransferAccountId == seed.MainId);
            debit.PostedAt = At(7);
            credit.PostedAt = At(8);
            await dbContext.SaveChangesAsync();
        }
        var completed = await Breakdown(client, profile.Id, "2026-09-07");
        Assert.Equal("completed", completed.PeriodStatus);
        Assert.Equal(new DateOnly(2026, 9, 7), completed.ObservedThrough);
        Assert.Equal(-200, completed.Totals.WithinScopeTransfers);
        Assert.Equal(410, completed.Totals.NetMovement);
        var current = await Breakdown(client, profile.Id, "2026-09-08");
        Assert.Equal(200, current.Totals.NetMovement);
        Assert.Equal(200, current.Totals.WithinScopeTransfers);
        Assert.Equal(0, current.Totals.ExternalCredits);
        var future = await Breakdown(client, profile.Id, "2026-09-15");
        Assert.Equal("future", future.PeriodStatus);
        Assert.Null(future.ObservedThrough);
        Assert.Equal(0, future.Totals.NetMovement);
        Assert.Equal(0, future.Totals.TransactionCount);
        Assert.Empty(future.Transactions.Items);
    }

    [Fact]
    public async Task UnmarkingATransferReturnsItToSpending()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var debit = await dbContext.Transactions.SingleAsync(x => x.AccountId == seed.MainId && x.InternalTransferAccountId == seed.SavingsId && x.Amount < 0);
            debit.Amount = -301;
            debit.InternalTransferAccountId = null;
            debit.InternalTransferSource = "excluded";
            await dbContext.SaveChangesAsync();
        }
        var unmarked = await Breakdown(client, profile.Id);
        Assert.Equal(401, unmarked.Totals.Spending);
        Assert.Equal(0, unmarked.Totals.SavingsTransfersOut);
        Assert.Equal(609, unmarked.Totals.NetMovement);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var debit = await dbContext.Transactions.SingleAsync(x => x.AccountId == seed.MainId && x.Amount == -301);
            debit.Amount = -300;
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(400, (await Breakdown(client, profile.Id)).Totals.Spending);
    }

    [Fact]
    public async Task AuditPaginationIsStableAndDoesNotChangeTheSummary()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        var ids = new List<Guid>();
        foreach (var page in Enumerable.Range(1, 4))
        {
            var data = (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profile.Id}/breakdown?date=2026-09-08&page={page}&pageSize=2"))!;
            Assert.Equal(8, data.Transactions.TotalCount);
            Assert.Equal(610, data.Totals.NetMovement);
            ids.AddRange(data.Transactions.Items.Select(x => x.Id));
        }
        Assert.Equal(8, ids.Distinct().Count());
        var savings = (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profile.Id}/breakdown?kind=savings-in"))!;
        Assert.Equal(1, savings.Transactions.TotalCount);
        Assert.Equal(50, Assert.Single(savings.Transactions.Items).Amount);
        Assert.Equal(610, savings.Totals.NetMovement);
    }

    [Fact]
    public async Task ProfilesAreTenantScopedAndVersionedIncludingDeletion()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-paycycles");
        var other = await Seed(factory, otherClient);
        Assert.Empty((await otherClient.GetFromJsonAsync<PayCycleProfileResponse[]>("/api/pay-cycles"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/pay-cycles/{profile.Id}/breakdown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(other))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/pay-cycles/{profile.Id}?expectedVersion=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await otherClient.PostAsJsonAsync("/api/pay-cycles", Request(seed))).StatusCode);
        var update = await client.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(seed, "monthly"));
        update.EnsureSuccessStatusCode();
        Assert.Equal(1, (await update.Content.ReadFromJsonAsync<PayCycleProfileResponse>())!.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(seed))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/pay-cycles/{profile.Id}?expectedVersion=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pay-cycles/{profile.Id}?expectedVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/pay-cycles/{profile.Id}/breakdown")).StatusCode);
    }

    [Theory]
    [InlineData("date=not-a-date")]
    [InlineData("date=9999-12-31")]
    [InlineData("date=1899-12-31")]
    [InlineData("page=banana")]
    [InlineData("page=0")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=101")]
    [InlineData("kind=salary")]
    public async Task InvalidBreakdownQueriesReturnBadRequest(string query)
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/pay-cycles/{profile.Id}/breakdown?{query}")).StatusCode);
    }

    [Fact]
    public async Task ProfileValidationRejectsOverlappingForeignCurrencyAndMissingScopes()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var valid = JsonSerializer.SerializeToNode(Request(seed))!;
        var badRequests = new[]
        {
            ("accountIds", "[]"), ("savingsAccountIds", $"[\"{seed.MainId}\"]"), ("currency", "\"USD\""),
            ("frequency", "\"daily\""), ("anchorDate", "\"9999-12-31\""), ("anchorDate", "null"),
            ("expectedIncome", "-1"), ("expectedIncome", "1.001"), ("name", "\"   \"")
        };
        foreach (var badRequest in badRequests)
        {
            var request = valid.DeepClone();
            request[badRequest.Item1] = System.Text.Json.Nodes.JsonNode.Parse(badRequest.Item2);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pay-cycles", request)).StatusCode);
        }
    }

    [Fact]
    public async Task BillingGateAppliesToReadsAndWrites()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var profile = await Create(client, seed);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var subscription = await dbContext.BillingSubscriptions.SingleAsync();
            subscription.Status = "canceled";
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync("/api/pay-cycles")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync($"/api/pay-cycles/{profile.Id}/breakdown")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/pay-cycles", Request(seed))).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PutAsJsonAsync($"/api/pay-cycles/{profile.Id}", Request(seed))).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.DeleteAsync($"/api/pay-cycles/{profile.Id}?expectedVersion=0")).StatusCode);
    }

    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task<PayCycleProfileResponse> Create(HttpClient client, SeedResult seed, string frequency = "fortnightly")
    {
        var response = await client.PostAsJsonAsync("/api/pay-cycles", Request(seed, frequency));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PayCycleProfileResponse>())!;
    }

    private static object Request(SeedResult seed, string frequency = "fortnightly") => new
    {
        name = "Family payday", frequency, anchorDate = "2026-09-01", currency = "AUD", expectedIncome = 1000,
        accountIds = new[] { seed.MainId, seed.SecondId }, savingsAccountIds = new[] { seed.SavingsId }, expectedVersion = 0
    };

    private static async Task<PayCycleBreakdownResponse> Breakdown(HttpClient client, Guid profileId, string date = "2026-09-08") =>
        (await client.GetFromJsonAsync<PayCycleBreakdownResponse>($"/api/pay-cycles/{profileId}/breakdown?date={date}"))!;

    private static async Task<SeedResult> Seed(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Pay-cycle family" });
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
            StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active",
            CurrentPeriodEnd = now.AddDays(30), CreatedAt = now, UpdatedAt = now
        });
        var main = new Account { TenantId = tenantId, Name = "Original", CustomName = "My everyday", IncludeInAnalyticsOverride = false, CreatedAt = now };
        var second = new Account { TenantId = tenantId, Name = "Spending", CreatedAt = now };
        var savings = new Account { TenantId = tenantId, Name = "Savings", CreatedAt = now };
        var other = new Account { TenantId = tenantId, Name = "Other own account", CreatedAt = now };
        dbContext.Accounts.AddRange(main, second, savings, other);
        dbContext.Transactions.AddRange(
            Row(main, 1000, 2), Row(main, -100, 2, "Shopping", " Groceries "),
            Row(main, -999, 2, status: "pending"), Row(main, -999, null),
            Row(main, -99, 2, currency: "USD"), Row(main, -777, 9), Row(main, 0, 8));
        AddTransfer(dbContext, main, second, 200, 3);
        AddTransfer(dbContext, main, savings, 300, 4);
        AddTransfer(dbContext, savings, main, 50, 5);
        AddTransfer(dbContext, main, other, 40, 6);
        await dbContext.SaveChangesAsync();
        return new SeedResult(tenantId, main.Id, second.Id, savings.Id);
    }

    private static Transaction Row(Account account, decimal amount, int? day, string? primaryCategory = null,
        string? secondaryCategory = null, string status = "posted", string currency = "AUD") => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = Guid.NewGuid().ToString(),
        Amount = amount, Currency = currency, PostedAt = day.HasValue ? At(day.Value) : null, Status = status,
        Description = "Synthetic transaction", PrimaryCategory = primaryCategory, SecondaryCategory = secondaryCategory, CreatedAt = At(1)
    };

    private static void AddTransfer(FinyteDbContext dbContext, Account debitAccount, Account creditAccount, decimal amount, int day)
    {
        var debit = Row(debitAccount, -amount, day);
        debit.InternalTransferAccountId = creditAccount.Id;
        debit.InternalTransferSource = "manual";
        var credit = Row(creditAccount, amount, day);
        credit.InternalTransferAccountId = debitAccount.Id;
        credit.InternalTransferSource = "manual";
        dbContext.Transactions.AddRange(debit, credit);
    }

    private static DateTimeOffset At(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);
    private sealed record SeedResult(Guid TenantId, Guid MainId, Guid SecondId, Guid SavingsId);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
