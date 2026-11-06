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

        var setup = await client.PutAsJsonAsync("/api/home-loan/setup", new
        {
            firstApplicant = "Alex", secondApplicant = "Sam", expectedVersion = 0,
            accounts = before.Setup.Accounts.Select(x => new { x.AccountId, x.Role })
        });
        setup.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/home-loan/setup", new { firstApplicant = "Alex", expectedVersion = 0, accounts = Array.Empty<object>() })).StatusCode);
        await Label(client, before, "ACME", "salary", 1);
        await Label(client, before, "BETACO", "salary", 2);
        await Label(client, before, "MUM", "gift", null);
        await Label(client, before, "INTEREST", "other-income", null);
        var rent = Assert.Single(before.Tags, x => x.Name == "Rent");
        Assert.Equal(("rent", true), (rent.Category, rent.IsDefault));
        (await client.PutAsJsonAsync("/api/home-loan/categories", new { tagId = rent.TagId, category = "rent" })).EnsureSuccessStatusCode();

        var report = await Get(client);
        var june = report.MonthlyTotals[0];
        Assert.Equal((4000m, 3000m, 5m, 0m), (june.FirstApplicantIncome, june.SecondApplicantIncome, june.OtherIncome, june.UnlabelledCredits));
        Assert.Equal((600m, 2000m, 1505m, 4405m), (june.LivingExpenses, june.Commitments, june.Saved, june.Surplus));
        var august = report.MonthlyTotals[2];
        Assert.Equal((750m, 5000m, 4255m), (august.LivingExpenses, august.Gifts, august.Surplus));
        Assert.Equal(1505m, report.Average.Saved);
        Assert.Equal(1500m, Assert.Single(report.Categories, x => x.Key == "savings-elsewhere").Total);
        var powerco = Assert.Single(report.NeedsLabel, x => x.Name == "Powerco");
        Assert.Equal((2, 100m), (powerco.Variants.Count, powerco.Total));
        Assert.Contains("Direct debit", powerco.PaymentTypes);
        Assert.Equal(2, powerco.Payments.Count);
        Assert.Equal(3, Assert.Single(report.Payers, x => x.Name == "Acme").Payments.Count);
        Assert.Equal(("salary", "other-income"), (Assert.Single(before.Payers, x => x.Name == "Betaco").SuggestedKind, Assert.Single(before.Payers, x => x.Name == "Interest").SuggestedKind));
        Assert.Null(Assert.Single(report.Payers, x => x.Name == "Acme").SuggestedKind);
        var savingsTransfer = Assert.Single(before.NeedsLabel, x => x.Name == "Car savings");
        Assert.Equal(("Savings", "Transfer"), (savingsTransfer.SuggestedTag, Assert.Single(savingsTransfer.PaymentTypes)));
        Assert.Equal(6000m, Assert.Single(report.Categories, x => x.Key == "rent").Total);
        Assert.False(Assert.Single(report.Tags, x => x.Name == "Rent").IsDefault);
        Assert.Equal(1500m, Assert.Single(report.Categories, x => x.Key == "food-groceries").Total);
        Assert.Equal(700m, Assert.Single(report.Categories, x => x.Key == "unlabelled").Total);
        Assert.Equal(3, Assert.Single(report.NeedsLabel, x => x.Name == "Mystery Shop").Count);
        Assert.Equal(1, Assert.Single(report.Flags, x => x.Kind == "gambling").Count);
        Assert.Equal(5000m, Assert.Single(report.Flags, x => x.Kind == "large-deposit").Total);
        Assert.Equal("Sam", report.Setup.SecondApplicant);

        var summary = await client.GetStringAsync("/api/home-loan/export/summary?months=3&end=2026-08");
        Assert.Contains("Income: Alex,Income: Sam", summary);
        Assert.Contains("Food & groceries,Living expense,500.00,1500.00", summary);
        Assert.Contains("Lenders will still ask for your official bank statements", summary);
        Assert.DoesNotContain("\n", summary.Replace("\r\n", ""));
        var transactions = await client.GetStringAsync("/api/home-loan/export/transactions?months=3&end=2026-08");
        Assert.Contains(transactions.Split("\r\n"), x => x.Contains("Salary,Alex"));
        Assert.Contains(transactions.Split("\r\n"), x => x.Contains("SPORTSBET") && x.EndsWith(",Gambling"));
        Assert.DoesNotContain("HOME LOAN REPAYMENT", transactions);
    }

    [Fact]
    public async Task MerchantNamesGroupSensiblyAndCanBeConfirmedSplitAndTagged()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClock(baseFactory);
        using var client = factory.CreateClient();
        await Seed(factory, client);
        Guid pets;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var everyday = await dbContext.Accounts.SingleAsync(x => x.Name == "Everyday");
            var tenantId = everyday.TenantId;
            var tag = new TransactionTag { TenantId = tenantId, Name = "Pets", Color = "#dddddd", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.TransactionTags.Add(tag);
            pets = tag.Id;
            dbContext.Transactions.AddRange(
                Row(everyday, 8, 3, -20m, "KMART 2001 SPRINGFIELD QL AUS Card xx1234 Value Date: 01/08/2026"),
                Row(everyday, 8, 4, -30m, "KMART 3002 SHELBYVILLE QL AUS"),
                Row(everyday, 8, 5, -10m, "7-ELEVEN 1234 SPRINGFIELD AU"),
                Row(everyday, 8, 6, -11m, "7-Eleven Stores Shelbyville AU AUS"),
                Row(everyday, 8, 7, -12m, "SP GRAINCO FOODS SPRINGFIELD VI AUS"),
                Row(everyday, 8, 8, -500m, "OCEANCO CRUISES SPRINGFIELD AU AUS"),
                Row(everyday, 8, 9, -100m, "THE GLENCO CLINIC PT GLENCO AU"),
                Row(everyday, 8, 10, -101m, "THE GLENCO CLINIC SHELBYVILLE QL AUS"),
                Row(everyday, 8, 11, -50m, "GLENCO CHEMPRO CHEMIS GLENCO AUS"),
                Row(everyday, 8, 14, -18.5m, "MAIN STREET CHEMIST GLENCO QL AUS Card xx1234 Value Date: 12/08/2026"),
                Row(everyday, 8, 15, -75m, "Transfer to xx4321 CommBank app"),
                Row(everyday, 8, 16, -95m, "CPA AUSTRALIA"),
                Row(everyday, 8, 12, -99m, "Direct Debit 123456 TEACHERS 998877"),
                Row(everyday, 8, 13, -99m, "TEACHERS HEALTH SPRINGFIELD VI AUS"),
                Row(everyday, 8, 1, 900m, "Salary GlenTech 12345 31/07/2026"),
                Row(everyday, 8, 2, 150m, "Direct Credit 111222 GLENTECH PTY LTD PAY000123"),
                Row(everyday, 8, 3, 80m, "Direct Credit 333444 MCARE BENEFITS 555666777 ABCD"),
                Row(everyday, 8, 4, 40m, "Fast Transfer From Robin Lee Smith Reimb dinner"));
            await dbContext.SaveChangesAsync();
        }
        var report = await Get(client);
        var names = report.NeedsLabel.Select(x => x.Name).ToList();
        Assert.Contains("Grainco Foods", names);
        Assert.Contains("Oceanco Cruises", names);
        Assert.Contains("Glenco Chempro", names);
        Assert.Contains("Transfer to account ending 4321", names);
        Assert.Contains("CPA", names);
        Assert.Equal("Health", Assert.Single(report.NeedsLabel, x => x.Name == "Main Street Chemist").SuggestedTag);
        Assert.Equal(2, Assert.Single(report.NeedsLabel, x => x.Name == "Kmart").Variants.Count);
        Assert.Equal(2, Assert.Single(report.NeedsLabel, x => x.Name == "7-Eleven").Variants.Count);
        var insurer = Assert.Single(report.NeedsLabel, x => x.Name.StartsWith("Teachers"));
        Assert.Equal((2, "Health"), (insurer.Variants.Count, insurer.SuggestedTag));
        var clinic = Assert.Single(report.NeedsLabel, x => x.Name == "Glenco Clinic");
        Assert.Equal(2, clinic.Variants.Count);
        Assert.False(clinic.Confirmed);
        var employer = Assert.Single(report.Payers, x => x.Name == "GlenTech");
        Assert.Equal((2, 1050m, "salary"), (employer.Count, employer.Total, employer.SuggestedKind));
        Assert.Equal("refund", Assert.Single(report.Payers, x => x.Name == "Mcare Benefits").SuggestedKind);
        Assert.Equal("refund", Assert.Single(report.Payers, x => x.Name == "Robin Lee Smith").SuggestedKind);

        (await client.PutAsJsonAsync("/api/merchant-vendors", new { assignments = new[]
        {
            new { statementName = clinic.Variants[0].RuleMerchantName, vendor = (string?)"Glenco Medical" },
            new { statementName = clinic.Variants[1].RuleMerchantName, vendor = (string?)clinic.Variants[1].OwnName }
        } })).EnsureSuccessStatusCode();
        report = await Get(client);
        Assert.True(Assert.Single(report.NeedsLabel, x => x.Name == "Glenco Medical").Confirmed);
        Assert.DoesNotContain(report.NeedsLabel, x => x.Name == "Glenco Clinic" && x.Variants.Count == 2);

        var kmart = Assert.Single(report.NeedsLabel, x => x.Name == "Kmart");
        var oddOne = kmart.Payments.Single(x => x.Amount == -30m);
        (await client.PutAsJsonAsync("/api/home-loan/payment-tag", new { transactionId = oddOne.TransactionId, tagId = pets })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/home-loan/merchant-tags", new { statementNames = kmart.Variants.Select(x => x.RuleMerchantName), tagName = "Home" })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var tagged = await dbContext.Transactions.Include(x => x.TagAssignments).ThenInclude(x => x.Tag)
                .Where(x => x.Description!.StartsWith("KMART")).ToListAsync();
            Assert.Equal(["Pets"], tagged.Single(x => x.Amount == -30m).TagAssignments.Select(x => x.Tag!.Name));
            Assert.Equal(["Home"], tagged.Single(x => x.Amount == -20m).TagAssignments.Select(x => x.Tag!.Name));
        }
        Assert.DoesNotContain((await Get(client)).NeedsLabel, x => x.Name == "Kmart");
        Guid home;
        using (var scope = factory.Services.CreateScope())
        {
            home = (await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().TransactionTags.SingleAsync(x => x.Name == "Home")).Id;
        }
        (await client.PutAsJsonAsync("/api/home-loan/merchant-tags/undo", new { statementNames = kmart.Variants.Select(x => x.RuleMerchantName), tagId = home })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/home-loan/payment-tag/undo", new { transactionId = oddOne.TransactionId, tagId = pets })).EnsureSuccessStatusCode();
        var undone = Assert.Single((await Get(client)).NeedsLabel, x => x.Name == "Kmart");
        Assert.Equal(2, undone.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/merchant-vendors", new { assignments = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/home-loan/payment-tag", new { transactionId = Guid.NewGuid(), tagName = "Home" })).StatusCode);
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

    private static async Task Label(HttpClient client, HomeLoanReport report, string name, string kind, int? applicant)
    {
        var payer = Assert.Single(report.Payers, x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        (await client.PutAsJsonAsync("/api/home-loan/income-sources", new { payer.Key, payer.Name, kind, applicant })).EnsureSuccessStatusCode();
    }

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
