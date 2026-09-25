using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class InternalTransferTests
{
    [Fact]
    public async Task DismissalsAndRepeatedDecisionsDoNotInvalidateFinancialProjections()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var pair = Assert.Single((await GetReview(client)).Items);
        async Task<long> Version()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Tenants
                .Where(x => x.Id == seed.TenantId).Select(x => x.FinancialDataVersion).SingleAsync();
        }
        var initial = await Version();
        (await Decide(client, pair, "dismiss")).EnsureSuccessStatusCode();
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        Assert.Equal(initial, await Version());
        (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
        var confirmed = await Version();
        Assert.True(confirmed > initial);
        (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
        Assert.Equal(confirmed, await Version());
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        Assert.True(await Version() > confirmed);
    }

    [Theory]
    [InlineData("Posted", "pOsTeD")]
    [InlineData("POSTED", "posted")]
    public async Task StatusCasingAgreesAcrossMatchingAndConfirmedAnalytics(string debitStatus, string creditStatus)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);
        var pair = Assert.Single((await GetReview(client)).Items);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            (await db.Transactions.SingleAsync(x => x.Id == pair.Debit.Id)).Status = debitStatus;
            (await db.Transactions.SingleAsync(x => x.Id == pair.Credit.Id)).Status = creditStatus;
            await db.SaveChangesAsync();
        }
        Assert.Single((await GetReview(client)).Items);
        (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
        Assert.Single((await GetReview(client, "confirmed")).Items);
        Assert.Empty((await GetReview(client, "needs-review")).Items);
        Assert.Equal(2500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlReviewFiltersAndPagesInTheDatabase()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"transfer_review_{Guid.NewGuid():N}";
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var factory = new FinyteApiFactory(new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            var seed = await Seed(factory, client);
            var pair = Assert.Single((await GetReview(client)).Items);
            (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                foreach (var index in Enumerable.Range(0, 55))
                {
                    var debit = Transaction(seed.TenantId, seed.DebitAccountId, -index - 200, 2);
                    var credit = Transaction(seed.TenantId, seed.CreditAccountId, index + 200, 2);
                    debit.Status = "Posted";
                    credit.Status = "pOsTeD";
                    db.Transactions.AddRange(debit, credit);
                    db.InternalTransfers.Add(new InternalTransfer { TenantId = seed.TenantId, DebitTransactionId = debit.Id, CreditTransactionId = credit.Id,
                        DebitAccountId = debit.AccountId, CreditAccountId = credit.AccountId, Amount = credit.Amount, Currency = "AUD", Status = "confirmed",
                        DebitPostedAt = debit.PostedAt!.Value, CreditPostedAt = credit.PostedAt!.Value, ReviewedByUserId = "dev-user" });
                }
                await db.SaveChangesAsync();
            }
            var first = await GetReview(client, "confirmed");
            var second = (await client.GetFromJsonAsync<TransferReviewPage>("/api/internal-transfers?from=2026-08-01&to=2026-09-30&status=confirmed&page=2"))!;
            Assert.Equal(56, first.TotalCount);
            Assert.Equal(50, first.Items.Count);
            Assert.Equal(6, second.Items.Count);
            Assert.Empty(first.Items.Select(x => x.Debit.Id).Intersect(second.Items.Select(x => x.Debit.Id)));
            Assert.Empty((await GetReview(client)).Items);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                (await db.Transactions.SingleAsync(x => x.Id == pair.Debit.Id)).Amount = -101;
                await db.SaveChangesAsync();
            }
            var stale = (await client.GetFromJsonAsync<TransferReviewPage>("/api/internal-transfers?from=2025-01-01&to=2025-02-01&status=needs-review"))!;
            Assert.Single(stale.Items);
            Assert.Equal(55, (await GetReview(client, "confirmed")).TotalCount);
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void DetectionKeepsEveryAmbiguousCandidateWithoutGreedyPairing()
    {
        var tenantId = Guid.NewGuid();
        var debit = Transaction(tenantId, Guid.NewGuid(), -100, 1);
        var credit = Transaction(tenantId, Guid.NewGuid(), 100, 2);
        var otherCredit = Transaction(tenantId, Guid.NewGuid(), 100, 4);
        Assert.Equal(2, InternalTransferService.FindCandidates([debit, credit, otherCredit]).Count);
    }

    [Theory]
    [InlineData("same-account")]
    [InlineData("other-family")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("pending")]
    [InlineData("no-date")]
    public void DetectionRejectsInvalidPairs(string scenario)
    {
        var tenantId = Guid.NewGuid();
        var debit = Transaction(tenantId, Guid.NewGuid(), -100, 1);
        var credit = Transaction(tenantId, Guid.NewGuid(), 100, 2);
        switch (scenario)
        {
            case "same-account": credit.AccountId = debit.AccountId; break;
            case "other-family": credit.TenantId = Guid.NewGuid(); break;
            case "currency": credit.Currency = "USD"; break;
            case "amount": credit.Amount = 99; break;
            case "date": credit.PostedAt = debit.PostedAt!.Value.AddDays(4); break;
            case "pending": credit.Status = "pending"; break;
            case "no-date": credit.PostedAt = null; break;
        }
        Assert.Empty(InternalTransferService.FindCandidates([debit, credit]));
    }

    [Fact]
    public async Task SuggestionsAreReadOnlyAndConfirmationExcludesBothLegsAcrossMonthAndAccountBoundaries()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var suggestions = await GetReview(client);
        Assert.Single(suggestions.Items);
        Assert.Equal("suggested", suggestions.Items[0].Status);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            Assert.Empty(await dbContext.InternalTransfers.ToListAsync());
        }
        var before = await CashFlow(client);
        Assert.Equal(12500, before.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(10000, before.DailyCashFlow.Sum(x => x.IncomeMinorUnits));

        (await Decide(client, suggestions.Items[0], "confirm")).EnsureSuccessStatusCode();
        var after = await CashFlow(client);
        Assert.Equal(2500, after.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(0, after.DailyCashFlow.Sum(x => x.IncomeMinorUnits));
        var raw = await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-08-01&to=2026-09-30&includeInternalTransfers=true");
        Assert.Equal(12500, raw!.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        var accountFlow = await client.GetFromJsonAsync<CashFlowRangeResponse>($"/api/cash-flow?from=2026-08-01&to=2026-09-30&accountId={seed.DebitAccountId}");
        Assert.Equal(2500, accountFlow!.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        using (var scope = factory.Services.CreateScope())
        {
            var projector = scope.ServiceProvider.GetRequiredService<IOverviewProjector>();
            var august = await projector.Rebuild(new OverviewProjectionScope(seed.TenantId, null, "2026-08"), CancellationToken.None);
            var september = await projector.Rebuild(new OverviewProjectionScope(seed.TenantId, null, "2026-09"), CancellationToken.None);
            Assert.Equal(2500, august.CurrentMonthSpendMinorUnits);
            Assert.Equal(2500, august.MonthlySpendByTag.Sum(x => x.AmountMinorUnits));
            Assert.Equal(0, september.CashFlowRace.IncomeMinorUnits);
            Assert.Equal(100000, august.AccountBalanceMinorUnits);
        }
        var transactionPage = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        Assert.Equal(2, transactionPage.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("isInternalTransfer").GetBoolean()));
        Assert.Empty((await GetReview(client)).Items);
        Assert.Single((await GetReview(client, "confirmed")).Items);
    }

    [Fact]
    public async Task DismissalSurvivesRefreshAndCanBeUndone()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);
        var pair = Assert.Single((await GetReview(client)).Items);
        (await Decide(client, pair, "dismiss")).EnsureSuccessStatusCode();
        Assert.Empty((await GetReview(client)).Items);
        Assert.Single((await GetReview(client, "dismissed")).Items);
        Assert.Equal(12500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        Assert.Single((await GetReview(client)).Items);
        (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        Assert.Single((await GetReview(client)).Items);
        Assert.Equal(12500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
    }

    [Fact]
    public async Task AmbiguousCandidatesAreLabelledAndOnlyOneCanBeConfirmed()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var credit = Transaction(seed.TenantId, seed.CreditAccountId, 100, 2);
            credit.PostedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
            dbContext.Transactions.Add(credit);
            await dbContext.SaveChangesAsync();
        }
        var pairs = (await GetReview(client)).Items;
        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, x => Assert.True(x.IsAmbiguous));
        (await Decide(client, pairs[0], "confirm")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(client, pairs[1], "confirm")).StatusCode);
        Assert.Single((await GetReview(client, "confirmed")).Items);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("pending")]
    [InlineData("currency")]
    [InlineData("account")]
    public async Task ChangedTransactionsStopBeingExcludedAndRequireReview(string change)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var pair = Assert.Single((await GetReview(client)).Items);
        (await Decide(client, pair, "confirm")).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var credit = await dbContext.Transactions.SingleAsync(x => x.Id == pair.Credit.Id);
            switch (change)
            {
                case "amount": credit.Amount = 90; break;
                case "date": credit.PostedAt = credit.PostedAt!.Value.AddDays(1); break;
                case "pending": credit.Status = "pending"; break;
                case "currency": credit.Currency = "USD"; break;
                case "account": credit.AccountId = seed.DebitAccountId; break;
            }
            await dbContext.SaveChangesAsync();
        }
        Assert.Empty((await GetReview(client, "confirmed")).Items);
        Assert.Single((await GetReview(client, "needs-review")).Items);
        Assert.Equal(12500, (await CashFlow(client)).DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(client, pair, "confirm")).StatusCode);
        (await Decide(client, pair, "reset")).EnsureSuccessStatusCode();
        Assert.Empty((await GetReview(client, "needs-review")).Items);
    }

    [Fact]
    public async Task OtherFamilyCannotSeeOrReviewPairsAndBillingIsRequired()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);
        var pair = Assert.Single((await GetReview(client)).Items);
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-transfers");
        otherClient.DefaultRequestHeaders.Add("X-Dev-User", "other-user");
        await Seed(factory, otherClient);
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(otherClient, pair, "confirm")).StatusCode);
        Assert.DoesNotContain((await GetReview(otherClient)).Items, x => x.Debit.Id == pair.Debit.Id);
        using var unpaidClient = factory.CreateClient();
        unpaidClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_unpaid-transfers");
        await unpaidClient.PostAsJsonAsync("/api/auth/family", new { name = "Unpaid" });
        Assert.Equal(HttpStatusCode.PaymentRequired, (await unpaidClient.GetAsync("/api/internal-transfers")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await Decide(unpaidClient, pair, "confirm")).StatusCode);
    }

    [Theory]
    [InlineData("from=2026-09-01&to=2026-08-01")]
    [InlineData("from=2024-01-01&to=2026-09-01")]
    [InlineData("to=0001-01-01")]
    [InlineData("status=bogus")]
    public async Task RejectsInvalidReviewFilters(string query)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/internal-transfers?{query}")).StatusCode);
    }

    [Fact]
    public async Task DashboardComparisonAndLedgerUseTheSameScopeWithoutOverwritingTheDefaultCache()
    {
        await using var factory = new FinyteApiFactory();
        await VerifyDashboardDrilldown(factory);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlDashboardComparisonAndLedgerUseTheSameScope()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"dashboard_test_{Guid.NewGuid():N}";
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var factory = new FinyteApiFactory(new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            await VerifyDashboardDrilldown(factory);
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyDashboardDrilldown(FinyteApiFactory factory)
    {
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        (await Decide(client, Assert.Single((await GetReview(client)).Items), "confirm")).EnsureSuccessStatusCode();
        var tagId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tag = new TransactionTag { Id = tagId, TenantId = seed.TenantId, Name = "Transfers", Color = "#aaaaaa" };
            dbContext.TransactionTags.Add(tag);
            var expense = await dbContext.Transactions.SingleAsync(x => x.TenantId == seed.TenantId && x.Amount == -25);
            expense.TagAssignments.Add(new TransactionTagAssignment { TransactionId = expense.Id, TagId = tagId });
            var pending = Transaction(seed.TenantId, seed.DebitAccountId, -55, 10);
            pending.Status = "pending";
            var undated = Transaction(seed.TenantId, seed.DebitAccountId, -75, 10);
            undated.PostedAt = null;
            undated.CreatedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
            var excludedAccount = new Account { TenantId = seed.TenantId, Name = "Excluded", IncludeInAnalyticsOverride = false };
            dbContext.Accounts.Add(excludedAccount);
            dbContext.Transactions.AddRange(pending, undated, Transaction(seed.TenantId, excludedAccount.Id, -200, 10));
            await dbContext.SaveChangesAsync();
            var projector = new OverviewProjector(dbContext);
            var projectionScope = new OverviewProjectionScope(seed.TenantId, null, "2026-08");
            var normal = await projector.Rebuild(projectionScope, CancellationToken.None);
            var comparison = await projector.ReadIncludingTransfers(projectionScope, CancellationToken.None);
            Assert.Equal(2500, normal.CurrentMonthSpendMinorUnits);
            Assert.Equal(12500, comparison.CurrentMonthSpendMinorUnits);
            Assert.Equal(normal.AccountBalanceMinorUnits, comparison.AccountBalanceMinorUnits);
            var persisted = await dbContext.OverviewProjections.SingleAsync(x => x.MonthKey == "2026-08");
            Assert.Equal(2500, JsonDocument.Parse(persisted.PayloadJson).RootElement.GetProperty("currentMonthSpendMinorUnits").GetInt64());
        }
        const string filters = "from=2026-08-01&to=2026-08-31&postedOnly=true&analyticsOnly=true&direction=debit";
        var normalPage = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude");
        Assert.Equal(1, normalPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(-2500, normalPage.GetProperty("items")[0].GetProperty("amountMinorUnits").GetInt64());
        var comparisonPage = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=include");
        Assert.Equal(2, comparisonPage.GetProperty("totalCount").GetInt32());
        var tagged = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&tagIds={tagId}");
        Assert.Equal(1, tagged.GetProperty("totalCount").GetInt32());
        var untagged = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&untagged=true");
        Assert.Equal(0, untagged.GetProperty("totalCount").GetInt32());
        var only = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=only");
        Assert.Equal(1, only.GetProperty("totalCount").GetInt32());
        Assert.True(only.GetProperty("items")[0].GetProperty("isInternalTransfer").GetBoolean());
        var direct = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?{filters}&internalTransfers=exclude&accountId={seed.DebitAccountId}");
        Assert.Equal(1, direct.GetProperty("totalCount").GetInt32());
        (await client.GetAsync("/api/overview?includeInternalTransfers=true")).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/overview/refresh?includeInternalTransfers=true", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/transactions?internalTransfers=bogus")).StatusCode);
    }

    private static async Task<TransferReviewPage> GetReview(HttpClient client, string status = "suggested") =>
        (await client.GetFromJsonAsync<TransferReviewPage>($"/api/internal-transfers?from=2026-08-01&to=2026-09-30&status={status}"))!;

    private static async Task<CashFlowRangeResponse> CashFlow(HttpClient client) =>
        (await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-08-01&to=2026-09-30"))!;

    private static Task<HttpResponseMessage> Decide(HttpClient client, TransferReview pair, string action) =>
        client.PostAsJsonAsync("/api/internal-transfers/review", new TransferDecisionRequest(pair.Debit.Id, pair.Credit.Id, action,
            pair.Credit.Amount, pair.Credit.Currency, pair.Debit.AccountId, pair.Credit.AccountId, pair.Debit.PostedAt, pair.Credit.PostedAt));

    private static Transaction Transaction(Guid tenantId, Guid accountId, decimal amount, int day) => new()
    {
        TenantId = tenantId, AccountId = accountId, Amount = amount, Currency = "AUD", Status = "posted",
        FiskilTransactionId = Guid.NewGuid().ToString(), Description = "Transfer test", CreatedAt = DateTimeOffset.UtcNow,
        PostedAt = new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero)
    };

    private static async Task<TransferSeed> Seed(FinyteApiFactory factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Transfer family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{tenantId:N}", CreatedAt = DateTimeOffset.UtcNow };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId,
            StripeSubscriptionId = $"sub_{tenantId:N}", StripePriceId = "price_test", Status = "active",
            CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(30), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        var debitAccount = new Account { TenantId = tenantId, Name = "Everyday", CurrentBalance = 300, BalanceAsOf = DateTimeOffset.UtcNow.AddMinutes(1), CreatedAt = DateTimeOffset.UtcNow };
        var creditAccount = new Account { TenantId = tenantId, Name = "Savings", CurrentBalance = 700, BalanceAsOf = DateTimeOffset.UtcNow.AddMinutes(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Accounts.AddRange(debitAccount, creditAccount);
        var debit = Transaction(tenantId, debitAccount.Id, -100, 31);
        var credit = Transaction(tenantId, creditAccount.Id, 100, 1);
        credit.PostedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Transactions.AddRange(debit, credit, Transaction(tenantId, debitAccount.Id, -25, 15));
        await dbContext.SaveChangesAsync();
        return new TransferSeed(tenantId, debitAccount.Id, creditAccount.Id);
    }

    private sealed record TransferSeed(Guid TenantId, Guid DebitAccountId, Guid CreditAccountId);
}
