using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class OverviewCurrencyScopeTests
{
    [Fact]
    public async Task TotalsCountOneCurrencyAndReportWhatThatExcluded()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client);

        using var scope = factory.Services.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<IOverviewProjector>();
        var overview = await projector.Rebuild(new OverviewProjectionScope(tenantId, null, "2026-08"), CancellationToken.None);

        Assert.Equal("AUD", overview.Currency);
        Assert.Equal(10000, overview.CashFlowRace.IncomeMinorUnits);
        Assert.Equal(2500, overview.CurrentMonthSpendMinorUnits);
        Assert.Equal(2500, overview.CashFlowRace.ExpenseMinorUnits);
        Assert.Equal(50000, overview.AccountBalanceMinorUnits);
        Assert.Equal(2500, overview.MonthlySpendByTag.Sum(x => x.AmountMinorUnits));
        Assert.Equal(2500, overview.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));

        var currencyScope = Assert.IsType<OverviewCurrencyScopeResponse>(overview.CurrencyScope);
        Assert.Equal(1, currencyScope.ExcludedAccounts);
        Assert.Equal(2, currencyScope.ExcludedTransactions);
        Assert.Equal(["USD"], currencyScope.ExcludedCurrencies);

        var coverage = Assert.IsType<OverviewBalanceCoverageResponse>(overview.BalanceCoverage);
        Assert.Equal(1, coverage.TotalAccounts);
        Assert.Equal(1, coverage.CoveredAccounts);
    }

    [Fact]
    public async Task ASingleCurrencyHouseholdReportsNothingExcluded()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var tenantId = await Seed(factory, client, includeForeignCurrency: false);

        using var scope = factory.Services.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<IOverviewProjector>();
        var overview = await projector.Rebuild(new OverviewProjectionScope(tenantId, null, "2026-08"), CancellationToken.None);

        Assert.Equal(10000, overview.CashFlowRace.IncomeMinorUnits);
        Assert.Equal(2500, overview.CurrentMonthSpendMinorUnits);
        Assert.Equal(50000, overview.AccountBalanceMinorUnits);
        var currencyScope = Assert.IsType<OverviewCurrencyScopeResponse>(overview.CurrencyScope);
        Assert.Equal(0, currencyScope.ExcludedAccounts);
        Assert.Equal(0, currencyScope.ExcludedTransactions);
        Assert.Empty(currencyScope.ExcludedCurrencies);
    }

    private static async Task<Guid> Seed(FinyteApiFactory factory, HttpClient client, bool includeForeignCurrency = true)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Currency family" });
        provision.EnsureSuccessStatusCode();
        var tenantId = (await provision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;

        var everyday = Account(tenantId, "Everyday", "AUD", 500m, now);
        dbContext.Accounts.Add(everyday);
        dbContext.Transactions.Add(Transaction(everyday, 100m, 12));
        dbContext.Transactions.Add(Transaction(everyday, -25m, 13));

        if (includeForeignCurrency)
        {
            var savings = Account(tenantId, "Savings", "USD", 900m, now);
            dbContext.Accounts.Add(savings);
            dbContext.Transactions.Add(Transaction(savings, 400m, 12));
            dbContext.Transactions.Add(Transaction(savings, -70m, 14));
        }

        await dbContext.SaveChangesAsync();
        return tenantId;
    }

    private static Account Account(Guid tenantId, string name, string currency, decimal balance, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        Name = name,
        Currency = currency,
        CurrentBalance = balance,
        BalanceAsOf = now,
        ManualBalanceVersion = 1,
        CreatedAt = now
    };

    private static Transaction Transaction(Account account, decimal amount, int day) => new()
    {
        TenantId = account.TenantId,
        Account = account,
        Amount = amount,
        Currency = account.Currency,
        Description = "Seeded",
        FiskilTransactionId = Guid.NewGuid().ToString("N"),
        Status = "posted",
        PostedAt = new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero),
        CreatedAt = new DateTimeOffset(2026, 8, day, 0, 0, 0, TimeSpan.Zero)
    };
}
