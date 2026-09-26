using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Core.Budgets;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class BudgetApiTests
{
    [Theory]
    [InlineData("monthly", "2025-01-31", "2025-02-28", "2025-02-28", "2025-03-30")]
    [InlineData("monthly", "2024-01-31", "2024-02-29", "2024-02-29", "2024-03-30")]
    [InlineData("monthly", "2025-01-31", "2025-03-31", "2025-03-31", "2025-04-29")]
    [InlineData("monthly", "2025-01-31", "2024-12-15", "2024-11-30", "2024-12-30")]
    [InlineData("weekly", "2025-09-01", "2025-08-31", "2025-08-25", "2025-08-31")]
    [InlineData("fortnightly", "2025-09-01", "2025-08-31", "2025-08-18", "2025-08-31")]
    public void PeriodBoundariesDoNotDriftAndWorkBeforeAnchor(string frequency, string anchor, string date, string from, string to)
    {
        var period = BudgetPeriods.Containing(frequency, DateOnly.Parse(anchor), DateOnly.Parse(date));
        Assert.Equal(DateOnly.Parse(from), period.From);
        Assert.Equal(DateOnly.Parse(to), period.To);
    }

    [Fact]
    public async Task CountsEachMatchingDebitOnceAndAuditPagesExplainTheEntireTotal()
    {
        await using var factory = new FinyteApiFactory();
        await VerifyScopeAndPaging(factory);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlMigrationsCountsPagingScopeAndCascadeDeletion()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"budget_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var scopedConnection = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
            await using var factory = new FinyteApiFactory(scopedConnection);
            using var client = factory.CreateClient();
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            await VerifyScopeAndPaging(factory);
            await VerifyPaddedCategories(factory, "org_padded-categories");
            var seed = await Seed(factory, client, "org_budget-cascade");
            var concurrentBudget = await Create(client, Request());
            var concurrentRequest = Request() with { ExpectedVersion = 0, Limit = 75 };
            var updates = await Task.WhenAll(
                client.PutAsJsonAsync($"/api/budgets/{concurrentBudget.Id}", concurrentRequest),
                client.PutAsJsonAsync($"/api/budgets/{concurrentBudget.Id}", concurrentRequest with { Limit = 80 }));
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict);
            var budget = await Create(client, Request() with { MatchMode = "selected", TagIds = [seed.TagId] });
            (await client.DeleteAsync($"/api/tags/{seed.TagId}")).EnsureSuccessStatusCode();
            var definitions = (await client.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!;
            var remaining = Assert.Single(definitions, x => x.Id == budget.Id);
            Assert.Equal("selected", remaining.MatchMode);
            Assert.Empty(remaining.TagIds);
            Assert.Equal(0, (await Period(client, budget.Id)).Spent);
            var selectedAccountBudget = await Create(client, Request() with { AccountScope = "selected", AccountIds = [seed.LoanAccountId] });
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                dbContext.Accounts.Remove(await dbContext.Accounts.SingleAsync(x => x.Id == seed.LoanAccountId));
                await dbContext.SaveChangesAsync();
            }
            Assert.Equal(0, (await Period(client, selectedAccountBudget.Id)).Spent);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task UpdatesAndDeletesRejectStaleVersionsAndPreserveDefinition()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var budget = await Create(client, Request());
        var edit = Request() with { Name = "Changed", Limit = 25, ExpectedVersion = 0, AccountScope = "selected", AccountIds = [seed.LoanAccountId] };
        (await client.PutAsJsonAsync($"/api/budgets/{budget.Id}", edit)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/budgets/{budget.Id}", edit with { Name = "Stale" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/budgets/{budget.Id}?expectedVersion=0")).StatusCode);
        Assert.Equal(900, (await Period(client, budget.Id)).Spent);
        var definitions = (await client.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!;
        Assert.Equal("Changed", Assert.Single(definitions).Name);
        (await client.PutAsJsonAsync($"/api/budgets/{budget.Id}", Request() with { ExpectedVersion = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(1189, (await Period(client, budget.Id)).Spent);
        (await client.DeleteAsync($"/api/budgets/{budget.Id}?expectedVersion=2")).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!);
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Transactions.AnyAsync());
    }

    [Fact]
    public async Task FamilyIsolationRejectsOtherFamiliesBudgetsTagsAndAccounts()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var budget = await Create(client, Request());
        using var other = factory.CreateClient();
        await Seed(factory, other, "org_budget-other");
        Assert.Empty((await other.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/budgets/{budget.Id}/periods")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/budgets/{budget.Id}/transactions?date=2025-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/budgets/{budget.Id}", Request() with { ExpectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/budgets/{budget.Id}?expectedVersion=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/budgets", Request() with { MatchMode = "selected", TagIds = [seed.TagId] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/budgets", Request() with { AccountScope = "selected", AccountIds = [seed.LoanAccountId] })).StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidDefinitionsQueriesAndMissingSubscriptions()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var invalid = new[]
        {
            Request() with { Name = " " }, Request() with { Limit = 0 }, Request() with { Limit = 1.001m },
            Request() with { Limit = 10000000000000000m }, Request() with { Currency = "US" }, Request() with { Frequency = "annual" },
            Request() with { AnchorDate = DateOnly.MinValue }, Request() with { MatchMode = "selected" },
            Request() with { AccountScope = "selected" }, Request() with { Categories = [" "] },
            Request() with { TagIds = [Guid.Empty] }, Request() with { MatchMode = "all", Categories = ["Groceries"] }
        };
        foreach (var request in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets", request)).StatusCode);
        }
        var budget = await Create(client, Request());
        var invalidQueries = new[] { "periods?date=nope", "periods?date=9999-12-31", "periods?count=13", "periods?count=0", "transactions?date=2025-09-01&page=0", "transactions?date=2025-09-01&pageSize=101", "transactions?date=2025-09-01&page=2147483647", "transactions?date=bad" };
        foreach (var query in invalidQueries)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/budgets/{budget.Id}/{query}")).StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            dbContext.BillingSubscriptions.RemoveRange(dbContext.BillingSubscriptions.Where(x => x.TenantId == seed.TenantId));
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.GetAsync("/api/budgets")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/api/budgets", Request())).StatusCode);
    }

    [Fact]
    public async Task FutureSpendingIsNotActualAndEmptyPeriodsRemainVisible()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.FirstAsync(x => x.TenantId == seed.TenantId);
            var transaction = Transaction(account, -500);
            transaction.PostedAt = new DateTimeOffset(future.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            dbContext.Transactions.Add(transaction);
            await dbContext.SaveChangesAsync();
        }
        var budget = await Create(client, Request());
        var periods = (await client.GetFromJsonAsync<PeriodsResponse>($"/api/budgets/{budget.Id}/periods?date={future:yyyy-MM-dd}&count=12"))!;
        Assert.Equal(12, periods.Periods.Length);
        Assert.Equal(0, periods.Periods[0].Spent);
        Assert.Null(periods.Periods[0].ObservedThrough);
        var page = (await client.GetFromJsonAsync<PageResponse>($"/api/budgets/{budget.Id}/transactions?date={future:yyyy-MM-dd}"))!;
        Assert.Equal(0, page.TotalCount);
    }

    private static async Task VerifyScopeAndPaging(FinyteApiFactory factory)
    {
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var request = Request() with { MatchMode = "selected", Categories = ["groceries"], TagIds = [seed.TagId, seed.SecondTagId] };
        var categories = (await client.GetFromJsonAsync<string[]>("/api/budgets/categories"))!;
        Assert.Single(categories, x => x.Equals("Groceries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Other", categories);
        var previewResponse = await client.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", request);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("2025-09-01", preview.GetProperty("from").GetString());
        Assert.Equal("2025-09-30", preview.GetProperty("to").GetString());
        Assert.Equal(190, preview.GetProperty("spent").GetDecimal());
        Assert.Equal(20, preview.GetProperty("transactionCount").GetInt32());
        Assert.Equal(5, preview.GetProperty("items").GetArrayLength());
        Assert.Equal("USD", preview.GetProperty("excludedCurrencies")[0].GetProperty("currency").GetString());
        Assert.Empty((await client.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!);
        var budget = await Create(client, request);
        var period = await Period(client, budget.Id);
        Assert.Equal(190, period.Spent);
        Assert.Equal(-90, period.Remaining);
        Assert.Equal(190, period.UsedPercent);
        Assert.Equal(20, period.TransactionCount);
        var excluded = Assert.Single(period.ExcludedCurrencies);
        Assert.Equal("USD", excluded.Currency);
        Assert.Equal(1, excluded.TransactionCount);
        var history = (await client.GetFromJsonAsync<PeriodsResponse>($"/api/budgets/{budget.Id}/periods?date=2025-09-09&count=12"))!;
        Assert.Equal(12, history.Periods.Length);
        Assert.Equal(period.Spent, history.Periods[0].Spent);
        Assert.All(history.Periods.Skip(2), x => Assert.Empty(x.ExcludedCurrencies));
        var all = new List<ItemResponse>();
        for (var page = 1; page <= 3; page++)
        {
            var result = (await client.GetFromJsonAsync<PageResponse>($"/api/budgets/{budget.Id}/transactions?date=2025-09-09&page={page}&pageSize=7"))!;
            Assert.Equal(20, result.TotalCount);
            all.AddRange(result.Items);
        }
        Assert.Equal(20, all.Select(x => x.Id).Distinct().Count());
        Assert.Equal(period.Spent, all.Sum(x => -x.Amount));
        Assert.Equal(1189, (await Period(client, (await Create(client, Request())).Id)).Spent);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var debit = await dbContext.Transactions.SingleAsync(x => x.Id == seed.TransferDebitId);
            debit.Amount = -51;
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(241, (await Period(client, budget.Id)).Spent);
    }

    [Fact]
    public async Task PaddedCategoriesAreSelectableAndCountTowardsSpending()
    {
        await using var factory = new FinyteApiFactory();
        await VerifyPaddedCategories(factory, "org_padded-categories");
    }

    [Fact]
    public async Task PreviewRejectsMissingCriteriaAndUsesAccountScopeWithoutSaving()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var invalid = new[]
        {
            Request() with { MatchMode = "selected" },
            Request() with { MatchMode = "selected", Categories = ["Grocereis"] },
            Request() with { MatchMode = "selected", TagIds = [Guid.NewGuid()] },
            Request() with { AccountScope = "selected", AccountIds = [Guid.NewGuid()] }
        };
        foreach (var request in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", request)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets", request)).StatusCode);
        }
        var scoped = Request() with { AccountScope = "selected", AccountIds = [seed.LoanAccountId] };
        var response = await client.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", scoped);
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(900, preview.GetProperty("spent").GetDecimal());
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        response = await client.PostAsJsonAsync($"/api/budgets/preview?date={future:yyyy-MM-dd}", scoped);
        response.EnsureSuccessStatusCode();
        preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, preview.GetProperty("transactionCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("observedThrough").ValueKind);
        Assert.Empty((await client.GetFromJsonAsync<BudgetResponse[]>("/api/budgets"))!);
        using var other = factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Dev-Organization", "org_empty-budget");
        (await other.PostAsJsonAsync("/api/auth/family", new { name = "Empty family" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.PaymentRequired, (await other.GetAsync("/api/budgets/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await other.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", scoped)).StatusCode);
    }

    [Fact]
    public async Task MissingCategoriesDoNotBroadenSavedBudgetsAndForeignCategoriesAreUnavailable()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var request = Request() with { MatchMode = "selected", Categories = ["Groceries"] };
        var budget = await Create(client, request);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var transactions = await dbContext.Transactions.Where(x => x.TenantId == seed.TenantId).ToListAsync();
            foreach (var transaction in transactions)
            {
                transaction.PrimaryCategory = "Family-only category";
                transaction.SecondaryCategory = null;
            }
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(0, (await Period(client, budget.Id)).Spent);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/budgets/{budget.Id}", request with { ExpectedVersion = 0 })).StatusCode);
        using var other = factory.CreateClient();
        await Seed(factory, other, "org_category-isolation");
        Assert.DoesNotContain("Family-only category", (await other.GetFromJsonAsync<string[]>("/api/budgets/categories"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", request with { Categories = ["Family-only category"] })).StatusCode);
    }

    private static async Task VerifyPaddedCategories(FinyteApiFactory factory, string organization)
    {
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client, organization);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var transaction = await dbContext.Transactions.SingleAsync(x => x.TenantId == seed.TenantId && x.Amount == -7);
            transaction.PrimaryCategory = "  Utilities  ";
            await dbContext.SaveChangesAsync();
        }
        var categories = (await client.GetFromJsonAsync<string[]>("/api/budgets/categories"))!;
        Assert.Contains("Utilities", categories);
        Assert.DoesNotContain(categories, x => x != x.Trim());
        var request = Request() with { MatchMode = "selected", Categories = ["Utilities"] };
        var response = await client.PostAsJsonAsync("/api/budgets/preview?date=2025-09-09", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(7, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spent").GetDecimal());
    }

    private static async Task<BudgetResponse> Create(HttpClient client, BudgetInput request)
    {
        var response = await client.PostAsJsonAsync("/api/budgets", request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BudgetResponse>())!;
    }

    private static async Task<PeriodResponse> Period(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/budgets/{id}/periods?date=2025-09-09&count=1");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PeriodsResponse>())!.Periods[0];
    }

    private static BudgetInput Request() => new("Spending", 100, "AUD", "monthly", new DateOnly(2025, 9, 1), "all", [], [], "analytics", [], null);

    private static async Task<SeedResult> Seed(FinyteApiFactory factory, HttpClient client, string? organization = null)
    {
        if (organization is not null)
        {
            client.DefaultRequestHeaders.Add("X-Dev-Organization", organization);
        }
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Budget family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var customer = new BillingCustomer { TenantId = tenantId, StripeCustomerId = $"cus_{Guid.NewGuid():N}", CreatedAt = now };
        dbContext.BillingCustomers.Add(customer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription { TenantId = tenantId, BillingCustomer = customer, StripeCustomerId = customer.StripeCustomerId, StripeSubscriptionId = $"sub_{Guid.NewGuid():N}", StripePriceId = "price_test", Status = "active", CurrentPeriodEnd = now.AddDays(30), CreatedAt = now, UpdatedAt = now });
        var account = new Account { TenantId = tenantId, Name = "Everyday", Currency = "AUD", CreatedAt = now };
        var loan = new Account { TenantId = tenantId, Name = "Home loan", AccountTypeOverride = "home-loan", CreatedAt = now };
        dbContext.Accounts.AddRange(account, loan);
        var tag = new TransactionTag { TenantId = tenantId, Name = "Food", Color = "#123456", CreatedAt = now };
        var secondTag = new TransactionTag { TenantId = tenantId, Name = "Shared", Color = "#abcdef", CreatedAt = now };
        dbContext.TransactionTags.AddRange(tag, secondTag);
        for (var index = 0; index < 18; index++)
        {
            var transaction = Transaction(account, -10);
            transaction.TagAssignments = [new TransactionTagAssignment { Tag = tag, CreatedAt = now }, new TransactionTagAssignment { Tag = secondTag, CreatedAt = now }];
            dbContext.Transactions.Add(transaction);
        }
        var tagged = Transaction(account, -7);
        tagged.PrimaryCategory = "Other";
        tagged.TagAssignments = [new TransactionTagAssignment { Tag = tag, CreatedAt = now }, new TransactionTagAssignment { Tag = secondTag, CreatedAt = now }];
        var secondary = Transaction(account, -3);
        secondary.PrimaryCategory = null;
        secondary.SecondaryCategory = "GROCERIES";
        var substring = Transaction(account, -999);
        substring.PrimaryCategory = "Groceries and Dining";
        var pending = Transaction(account, -100);
        pending.Status = "pending";
        var undated = Transaction(account, -100);
        undated.PostedAt = null;
        var foreignCurrency = Transaction(account, -100);
        foreignCurrency.Currency = "USD";
        var after = Transaction(account, -80);
        after.PostedAt = new DateTimeOffset(2025, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var before = Transaction(account, -4);
        // A provider-stamped local evening stays on its own calendar day.
        before.PostedAt = new DateTimeOffset(2025, 8, 31, 23, 59, 59, TimeSpan.FromHours(10));
        var debit = Transaction(account, -50);
        var credit = Transaction(loan, 50);
        dbContext.Transactions.AddRange(tagged, secondary, substring, pending, undated, foreignCurrency, after, before, debit, credit, Transaction(account, 100), Transaction(loan, -900));
        dbContext.InternalTransfers.Add(new InternalTransfer
        {
            TenantId = tenantId, DebitTransaction = debit, CreditTransaction = credit, DebitAccountId = account.Id, CreditAccountId = loan.Id,
            DebitPostedAt = debit.PostedAt!.Value, CreditPostedAt = credit.PostedAt!.Value, Amount = 50, Currency = "AUD", Status = "confirmed", ReviewedByUserId = "test", UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        return new SeedResult(tenantId, tag.Id, secondTag.Id, loan.Id, debit.Id);
    }

    private static Transaction Transaction(Account account, decimal amount) => new()
    {
        TenantId = account.TenantId, Account = account, Amount = amount, PrimaryCategory = "Groceries", Description = "Test purchase",
        FiskilTransactionId = Guid.NewGuid().ToString("N"), Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2025, 9, 9, 0, 0, 0, TimeSpan.Zero), CreatedAt = DateTimeOffset.UtcNow
    };
    private sealed record BudgetInput(string Name, decimal Limit, string Currency, string Frequency, DateOnly AnchorDate, string MatchMode, string[] Categories, Guid[] TagIds, string AccountScope, Guid[] AccountIds, int? ExpectedVersion);
    private sealed record SeedResult(Guid TenantId, Guid TagId, Guid SecondTagId, Guid LoanAccountId, Guid TransferDebitId);
    private sealed record BudgetResponse(Guid Id, string Name, string MatchMode, Guid[] TagIds);
    private sealed record PeriodsResponse(PeriodResponse[] Periods);
    private sealed record ExcludedCurrencyResponse(string Currency, int TransactionCount);
    private sealed record PeriodResponse(decimal Spent, decimal Remaining, decimal UsedPercent, int TransactionCount, DateOnly? ObservedThrough, ExcludedCurrencyResponse[] ExcludedCurrencies);
    private sealed record PageResponse(int TotalCount, ItemResponse[] Items);
    private sealed record ItemResponse(Guid Id, decimal Amount);
}
