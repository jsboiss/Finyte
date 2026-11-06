using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.HomeLoans;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class HomeLoanApiTests
{
    private const string Report = "/api/home-loan?months=3&end=2026-08";

    [Fact]
    public async Task LabelledIncomeSpendingAndSavingsAddUpPerMonthAndExport()
    {
        await using var baseFactory = new FinyteApiFactory();
        await Scenario(baseFactory);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlLabelledIncomeSpendingAndSavingsAddUpPerMonthAndExport()
    {
        var connection = Environment.GetEnvironmentVariable("FINYTE_TEST_POSTGRES")!;
        var schema = $"homeloan_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var baseFactory = new FinyteApiFactory(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            using (var scope = baseFactory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Database.MigrateAsync();
            }
            await Scenario(baseFactory);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task Scenario(FinyteApiFactory baseFactory)
    {
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client);

        var before = await Get(client);
        Assert.Equal(["2026-06", "2026-07", "2026-08"], before.MonthlyTotals.Select(x => x.Month));
        Assert.Equal(7005m, before.MonthlyTotals[0].UnlabelledCredits);
        Assert.Equal("excluded", Assert.Single(before.Setup.Accounts, x => x.Name == "Home loan").Role);
        Assert.Equal("savings", Assert.Single(before.Setup.Accounts, x => x.Name == "Savings").Role);
        Assert.All(before.Tags, x => Assert.Null(x.Category));
        Assert.Equal(3, before.Payers.Count(x => x.Name.StartsWith("SALARY ACME", StringComparison.Ordinal)));

        var setup = await client.PutAsJsonAsync("/api/home-loan/setup", new
        {
            firstApplicant = "Alex", secondApplicant = "Sam", expectedVersion = 0,
            accounts = before.Setup.Accounts.Select(x => new { x.AccountId, x.Role })
        });
        setup.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/home-loan/setup", new { firstApplicant = "Alex", expectedVersion = 0, accounts = Array.Empty<object>() })).StatusCode);
        await Label(client, "Salary ACME pty ltd", "salary", 1);
        await Label(client, "PAY BETACO PAYROLL", "salary", 2);
        await Label(client, "TRANSFER FROM MUM", "gift", null);
        await Label(client, "INTEREST", "other-income", null);
        await Map(client, before, "Rent", "rent");
        await Map(client, before, "Groceries", "food-groceries");
        await Map(client, before, "Savings", "savings-elsewhere");

        var report = await Get(client);
        var june = report.MonthlyTotals[0];
        Assert.Equal((4000m, 3000m, 5m, 0m), (june.FirstApplicantIncome, june.SecondApplicantIncome, june.OtherIncome, june.UnlabelledCredits));
        Assert.Equal((600m, 2000m, 1505m, 4405m), (june.LivingExpenses, june.Commitments, june.Saved, june.Surplus));
        var august = report.MonthlyTotals[2];
        Assert.Equal((750m, 5000m, 4255m), (august.LivingExpenses, august.Gifts, august.Surplus));
        Assert.Equal(1505m, report.Average.Saved);
        Assert.Equal(1500m, Assert.Single(report.Categories, x => x.Key == "savings-elsewhere").Total);
        Assert.Equal(6000m, Assert.Single(report.Categories, x => x.Key == "rent").Total);
        Assert.Equal(1500m, Assert.Single(report.Categories, x => x.Key == "food-groceries").Total);
        Assert.Equal(700m, Assert.Single(report.Categories, x => x.Key == "untagged").Total);
        Assert.Equal((7, 700m), (report.Untagged.Count, report.Untagged.Total));
        var acme = report.Payers.Where(x => x.Name.StartsWith("SALARY ACME", StringComparison.Ordinal)).ToList();
        Assert.All(acme, x => Assert.Equal(("salary", "salary acme pty ltd"), (x.Kind, x.MatchedBy)));
        Assert.Equal("rent", Assert.Single(report.Tags, x => x.Name == "Rent").Category);
        Assert.Equal(3, Assert.Single(report.Tags, x => x.Name == "Rent").Payments.Count);
        Assert.Equal(5000m, Assert.Single(report.Flags, x => x.Kind == "large-deposit").Total);
        Assert.DoesNotContain(report.LenderCategories, x => x.Key == "not-expense");
        Assert.Equal("Sam", report.Setup.SecondApplicant);

        var summary = await client.GetStringAsync("/api/home-loan/export/summary?months=3&end=2026-08");
        Assert.Contains("Income: Alex,Income: Sam", summary);
        Assert.Contains("Food & groceries,Living expense,500.00,1500.00", summary);
        Assert.Contains("Lenders will still ask for your official bank statements", summary);
        Assert.DoesNotContain("\n", summary.Replace("\r\n", ""));
        var transactions = await client.GetStringAsync("/api/home-loan/export/transactions?months=3&end=2026-08");
        Assert.Contains(transactions.Split("\r\n"), x => x.Contains("Salary,Alex"));
        Assert.Contains(transactions.Split("\r\n"), x => x.Contains("SPORTSBET") && x.Contains("Not tagged yet"));
        Assert.DoesNotContain("HOME LOAN REPAYMENT", transactions);
    }

    [Fact]
    public async Task LenderCategoriesComeOnlyFromExplicitChoicesAndConflictsNeedReview()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Guid conflict;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var everyday = await dbContext.Accounts.SingleAsync(x => x.Name == "Everyday");
            var tags = await dbContext.TransactionTags.ToDictionaryAsync(x => x.Name);
            var tenantId = everyday.TenantId;
            TransactionTag Add(string name)
            {
                var tag = new TransactionTag { TenantId = tenantId, Name = name, Color = "#dddddd", CreatedAt = DateTimeOffset.UtcNow };
                dbContext.TransactionTags.Add(tag);
                return tag;
            }
            var shared = Add("Shared");
            var transfers = Add("Transfers");
            var home = Add("Home");
            var hardware = Tagged(Tagged(Row(everyday, 8, 21, -300m, "HARDWARE CO SPRINGFIELD"), home), tags["Groceries"]);
            conflict = hardware.Id;
            dbContext.Transactions.AddRange(
                Tagged(Tagged(Row(everyday, 8, 19, -2000m, "LANDLORD CO"), tags["Rent"]), shared),
                Tagged(Row(everyday, 8, 20, -1000m, "PROPERTY MANAGER CO"), transfers),
                hardware,
                Row(everyday, 8, 23, 100m, "Transfer from xx1234 CommBank app"),
                Row(everyday, 8, 24, 200m, "Transfer from xx5678 CommBank app"));
            await dbContext.SaveChangesAsync();
        }
        var before = await Get(client);
        await Map(client, before, "Rent", "rent");
        await Map(client, before, "Home", "residence");
        await Map(client, before, "Groceries", "food-groceries");

        var report = await Get(client);
        Assert.Equal(8000m, Assert.Single(report.Categories, x => x.Key == "rent").Total);
        Assert.Equal(1000m + 1500m, Assert.Single(report.Categories, x => x.Key == "unmapped").Total);
        Assert.Equal(300m, Assert.Single(report.Categories, x => x.Key == "review").Total);
        Assert.Equal(conflict, Assert.Single(report.Review).TransactionId);
        Assert.Equal(1000m + 1500m + 300m + 1500m + 700m, report.Total.LivingExpenses);
        Assert.Equal(2, report.Payers.Count(x => x.Name.StartsWith("Transfer from xx", StringComparison.Ordinal)));

        (await client.PutAsJsonAsync("/api/home-loan/payment-category", new { transactionId = conflict, category = "residence" })).EnsureSuccessStatusCode();
        await Label(client, "Transfer from xx1234", "other-income", 1);
        report = await Get(client);
        Assert.Equal(300m, Assert.Single(report.Categories, x => x.Key == "residence").Total);
        Assert.DoesNotContain(report.Categories, x => x.Key == "review");
        Assert.Equal("residence", Assert.Single(report.Review).OwnCategory);
        Assert.Equal("other-income", Assert.Single(report.Payers, x => x.Name.Contains("xx1234")).Kind);
        Assert.Null(Assert.Single(report.Payers, x => x.Name.Contains("xx5678")).Kind);

        (await client.PutAsJsonAsync("/api/home-loan/payment-category", new { transactionId = conflict, category = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal(conflict, Assert.Single((await Get(client)).Review).TransactionId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/home-loan/payment-category", new { transactionId = conflict, category = "not-expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/home-loan/payment-category", new { transactionId = Guid.NewGuid(), category = "rent" })).StatusCode);
    }

    [Fact]
    public async Task InvalidRequestsAreRejected()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/home-loan?months=4")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/home-loan?end=2026-13")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/home-loan/export/other")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/home-loan/income-sources", new { key = "acme", name = "ACME", kind = "lottery" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/home-loan/setup", new { firstApplicant = "", expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/home-loan/categories", new { tagId = Guid.NewGuid(), category = "rent" })).StatusCode);
    }

    private static async Task Label(HttpClient client, string key, string kind, int? applicant) =>
        (await client.PutAsJsonAsync("/api/home-loan/income-sources", new { key, name = key, kind, applicant })).EnsureSuccessStatusCode();

    private static async Task Map(HttpClient client, HomeLoanReport report, string tag, string category) =>
        (await client.PutAsJsonAsync("/api/home-loan/categories", new { Assert.Single(report.Tags, x => x.Name == tag).TagId, category })).EnsureSuccessStatusCode();

    private static async Task<HomeLoanReport> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<HomeLoanReport>(Report, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    private static WebApplicationFactory<Program> WithClock(FinyteApiFactory factory) => factory.WithWebHostBuilder(x =>
        x.ConfigureServices(y => y.AddSingleton<TimeProvider>(new FixedClock())));

    private static async Task Seed(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Home loan family" });
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
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", CreatedAt = now };
        var savings = new Account { TenantId = tenantId, Name = "Savings", AccountTypeOverride = "savings", CreatedAt = now };
        var loan = new Account { TenantId = tenantId, Name = "Home loan", AccountTypeOverride = "home-loan", CreatedAt = now };
        var groceries = new TransactionTag { TenantId = tenantId, Name = "Groceries", Color = "#aaaaaa", CreatedAt = now };
        var rent = new TransactionTag { TenantId = tenantId, Name = "Rent", Color = "#bbbbbb", CreatedAt = now };
        var saving = new TransactionTag { TenantId = tenantId, Name = "Savings", Color = "#cccccc", CreatedAt = now };
        dbContext.Accounts.AddRange(everyday, savings, loan);
        dbContext.TransactionTags.AddRange(groceries, rent, saving);
        foreach (var month in new[] { 6, 7, 8 })
        {
            dbContext.Transactions.AddRange(
                Row(everyday, month, 15, 4000m, $"SALARY ACME PTY LTD 1234{month}"),
                Row(everyday, month, 20, 3000m, "PAY BETACO PAYROLL"),
                Tagged(Row(everyday, month, 10, -500m, "GROCERCO 123 SPRINGFIELD"), groceries),
                Tagged(Row(everyday, month, 1, -2000m, "RENT PAYMENT AGENT"), rent),
                Row(everyday, month, 12, -100m, "MYSTERY SHOP SPRINGFIELD"),
                Tagged(Row(everyday, month, 25, -500m, "Transfer to other Bank NetBank savings"), saving),
                Row(savings, month, 28, 5m, "INTEREST"),
                Row(loan, month, 3, -300m, "HOME LOAN REPAYMENT"));
            var toSavings = Row(everyday, month, 16, -1000m, "Transfer to Savings");
            var intoSavings = Row(savings, month, 16, 1000m, "Transfer from Everyday");
            toSavings.InternalTransferAccountId = savings.Id;
            intoSavings.InternalTransferAccountId = everyday.Id;
            dbContext.Transactions.AddRange(toSavings, intoSavings);
        }
        dbContext.Transactions.AddRange(Row(everyday, 8, 22, -50m, "SPORTSBET MELBOURNE"), Row(everyday, 8, 2, 5000m, "TRANSFER FROM MUM"),
            Row(everyday, 8, 5, -40m, "Direct Debit 123456 POWERCO 998877"), Row(everyday, 8, 18, -60m, "POWERCO RETAIL SPRINGFIELD VI AUS"),
            Row(everyday, 7, 9, -250m, "Transfer to other Bank NetBank car savings"));
        await dbContext.SaveChangesAsync();
    }

    private static Transaction Tagged(Transaction transaction, TransactionTag tag)
    {
        transaction.TagAssignments.Add(new TransactionTagAssignment { TransactionId = transaction.Id, TagId = tag.Id, Tag = tag, Source = TransactionTagSource.Manual, CreatedAt = DateTimeOffset.UtcNow });
        return transaction;
    }

    private static Transaction Row(Account account, int month, int day, decimal amount, string text) => new()
    {
        TenantId = account.TenantId, Account = account, AccountId = account.Id, FiskilTransactionId = $"file:{Guid.NewGuid():N}", Amount = amount,
        Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, month, day, 2, 0, 0, TimeSpan.Zero), MerchantName = text, Description = text
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
