using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Core.ProviderSync;
using Finyte.Data;
using Finyte.Data.Analytics;
using Finyte.Data.ProviderSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class AccountPreferencesApiTests
{
    [Fact]
    public async Task ExcludedForeignAccountDoesNotChooseTheSpendingCurrency()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
        account.CustomName = "A foreign loan";
        account.Currency = "USD";
        account.IncludeInAnalyticsOverride = false;
        await dbContext.SaveChangesAsync();
        var projector = new OverviewProjector(dbContext);
        var pending = await new ProjectionDispatcher(dbContext, projector).GetOrRebuildOverview(
            new OverviewProjectionScope(seed.TenantId, null, "2026-09"), CancellationToken.None);
        Assert.Equal("AUD", pending.Currency);
        var combined = await projector.Rebuild(new OverviewProjectionScope(seed.TenantId, null, "2026-09"), CancellationToken.None);
        Assert.Equal("AUD", combined.Currency);
        Assert.Equal(5000, combined.CurrentMonthSpendMinorUnits);
        var cashFlow = (await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-09-01&to=2026-09-30"))!;
        Assert.Equal("AUD", cashFlow.Currency);
    }

    [Fact]
    public async Task ManualBalanceAcceptsDecimalStringsUsedByTheForm()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new
        {
            currentBalance = "-999.25", availableBalance = "25.10", expectedVersion = 0
        })).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var account = await scope.ServiceProvider.GetRequiredService<FinyteDbContext>().Accounts.SingleAsync(x => x.Id == seed.AccountId);
        Assert.Equal(-999.25m, account.CurrentBalance);
        Assert.Equal(25.10m, account.AvailableBalance);
    }

    [Fact]
    public async Task PreferencesSurviveBankSyncAndCanBeResetToUpdatedProviderDefaults()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var update = await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new
        {
            customName = " Family savings ", accountTypeOverride = "savings", includeInAnalyticsOverride = false, expectedVersion = 0
        });
        update.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            account.FiskilAccountId = "bank-account";
            var member = await dbContext.TenantMembers.FirstAsync(x => x.TenantId == seed.TenantId);
            dbContext.ProviderConnections.Add(new ProviderConnection
            {
                TenantId = seed.TenantId, TenantMemberId = member.Id, EndUserId = "end-user", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
            var bankingClient = new AccountBankingClient();
            await new FiskilBankingSyncService(dbContext, bankingClient, new Finyte.Data.Tagging.TransactionTagService(dbContext)).SyncAccounts(new ProviderSyncRun
            {
                TenantId = seed.TenantId, Provider = "fiskil", Dataset = "accounts", Status = "running", EndUserId = "end-user", CreatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
        var accountResponse = Assert.Single((await client.GetFromJsonAsync<AccountResponse[]>("/api/accounts"))!, x => x.Id == seed.AccountId);
        Assert.Equal("Family savings", accountResponse.Name);
        Assert.Equal("Renamed by bank", accountResponse.OriginalName);
        Assert.Equal("savings", accountResponse.AccountType);
        Assert.Equal("home-loan", accountResponse.InferredAccountType);
        Assert.True(accountResponse.IsProviderManaged);
        Assert.False(accountResponse.IncludeInAnalytics);
        Assert.Equal(1, accountResponse.PreferencesVersion);

        var reset = await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new
        {
            customName = (string?)null, accountTypeOverride = (string?)null, includeInAnalyticsOverride = (bool?)null, expectedVersion = 1
        });
        reset.EnsureSuccessStatusCode();
        var result = (await reset.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Equal("Renamed by bank", result.Name);
        Assert.Equal("home-loan", result.AccountType);
        Assert.False(result.IncludeInAnalytics);
        Assert.Null(result.IncludeInAnalyticsOverride);
    }

    [Fact]
    public async Task PreferencesAreFamilyScopedAndRejectStaleEditsWithoutOverwritingNewValues()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-Dev-Organization", "org_other-accounts");
        await Seed(factory, otherClient);
        var request = new { customName = "Our name", accountTypeOverride = "offset", includeInAnalyticsOverride = (bool?)null, expectedVersion = 0 };
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = 1, expectedVersion = 0 })).StatusCode);
        (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", request)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new { customName = "Stale name", expectedVersion = 0 })).StatusCode);
        var result = Assert.Single((await client.GetFromJsonAsync<AccountResponse[]>("/api/accounts"))!, x => x.Id == seed.AccountId);
        Assert.Equal("Our name", result.Name);
        Assert.True(result.IncludeInAnalytics);
        var transactions = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        Assert.Contains(transactions.GetProperty("items").EnumerateArray(), x => x.GetProperty("accountDisplayName").GetString() == "Our name");
    }

    [Fact]
    public async Task CombinedSpendingUsesPreferencesButBalanceAndDirectInspectionRemainAvailable()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var before = await new OverviewProjector(dbContext).Rebuild(new OverviewProjectionScope(seed.TenantId, null, "2026-09"), CancellationToken.None);
            Assert.Equal(35000, before.CurrentMonthSpendMinorUnits);
            Assert.Equal(-1900000, before.AccountBalanceMinorUnits);
        }
        (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new
        {
            customName = "Home loan", accountTypeOverride = "home-loan", includeInAnalyticsOverride = (bool?)null, expectedVersion = 0
        })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            Assert.Equal(ProjectionStatus.Pending, (await dbContext.OverviewProjections.SingleAsync()).Status);
            Assert.True((await dbContext.Tenants.SingleAsync()).FinancialDataVersion > 0);
            var projector = new OverviewProjector(dbContext);
            var combined = await projector.Rebuild(new OverviewProjectionScope(seed.TenantId, null, "2026-09"), CancellationToken.None);
            var direct = await projector.Rebuild(new OverviewProjectionScope(seed.TenantId, seed.AccountId, "2026-09"), CancellationToken.None);
            Assert.Equal(5000, combined.CurrentMonthSpendMinorUnits);
            Assert.Equal(-1900000, combined.AccountBalanceMinorUnits);
            Assert.Equal(30000, direct.CurrentMonthSpendMinorUnits);
            Assert.Equal("Home loan", direct.Scope.Label);
            Assert.Equal(-2000000, direct.AccountBalanceMinorUnits);
        }
        var combinedCashFlow = (await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-09-01&to=2026-09-30"))!;
        var directCashFlow = (await client.GetFromJsonAsync<CashFlowRangeResponse>($"/api/cash-flow?from=2026-09-01&to=2026-09-30&accountId={seed.AccountId}"))!;
        Assert.Equal(5000, combinedCashFlow.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        Assert.Equal(30000, directCashFlow.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new
        {
            accountTypeOverride = "home-loan", includeInAnalyticsOverride = true, expectedVersion = 1
        })).EnsureSuccessStatusCode();
        combinedCashFlow = (await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-09-01&to=2026-09-30"))!;
        Assert.Equal(35000, combinedCashFlow.DailyCashFlow.Sum(x => x.ExpenseMinorUnits));
        var transactions = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        Assert.Equal(2, transactions.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ManualBalanceUpdatesSnapshotWithoutTransactionsAndCannotEditProviderBalance()
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        var response = await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = -999.25m, availableBalance = (decimal?)null, expectedVersion = 0 });
        response.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            Assert.Equal(-999.25m, account.CurrentBalance);
            Assert.Null(account.AvailableBalance);
            Assert.NotNull(account.BalanceAsOf);
            Assert.Equal("AUD", account.Currency);
            Assert.Equal(2, await dbContext.Transactions.CountAsync());
            Assert.Equal(1, account.ManualBalanceVersion);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = 100, expectedVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = 1.001m, expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = 10000000000000000m, expectedVersion = 1 })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
            var account = await dbContext.Accounts.SingleAsync(x => x.Id == seed.AccountId);
            // Disconnected provider accounts still retain their provider identity and must remain protected.
            account.FiskilAccountId = "disconnected-bank-account";
            await dbContext.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/balance", new { currentBalance = 0, expectedVersion = 1 })).StatusCode);
    }

    [Theory]
    [InlineData("unsupported", "Name", 0)]
    [InlineData("", "Name", 0)]
    [InlineData("savings", "Name", -1)]
    [InlineData("savings", "Name", null)]
    public async Task RejectsInvalidPreferences(string type, string name, int? version)
    {
        await using var factory = new FinyteApiFactory();
        using var client = factory.CreateClient();
        var seed = await Seed(factory, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/accounts/{seed.AccountId}/preferences", new { customName = name, accountTypeOverride = type, expectedVersion = version })).StatusCode);
    }

    [Theory]
    [InlineData("TRANS_AND_SAVINGS_ACCOUNTS", "everyday", true)]
    [InlineData("RESIDENTIAL_MORTGAGES", "home-loan", false)]
    [InlineData("future-category", "other", true)]
    [InlineData(null, "other", true)]
    public void ProviderDefaultsAreConservativeAndUnknownCategoriesRemainVisible(string? category, string expectedType, bool included)
    {
        var account = new Account { Name = "Account", ProductCategory = category };
        Assert.Equal(expectedType, AccountPreferences.EffectiveType(account));
        Assert.Equal(included, AccountPreferences.IncludeInAnalytics(account));
    }

    private static async Task<SeedResult> Seed(FinyteApiFactory factory, HttpClient client)
    {
        var provision = await client.PostAsJsonAsync("/api/auth/family", new { name = "Accounts family" });
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
        var account = new Account { TenantId = tenantId, Name = "Unclassified loan", CurrentBalance = -20000, CreatedAt = now };
        var everyday = new Account { TenantId = tenantId, Name = "Everyday", CurrentBalance = 1000, CreatedAt = now };
        dbContext.Accounts.AddRange(account, everyday);
        dbContext.Transactions.AddRange(
            new Transaction { TenantId = tenantId, Account = account, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = -300, Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), CreatedAt = now },
            new Transaction { TenantId = tenantId, Account = everyday, FiskilTransactionId = Guid.NewGuid().ToString(), Amount = -50, Currency = "AUD", Status = "posted", PostedAt = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero), CreatedAt = now });
        await dbContext.SaveChangesAsync();
        return new SeedResult(tenantId, account.Id);
    }

    private sealed record SeedResult(Guid TenantId, Guid AccountId);
    private sealed record AccountResponse(Guid Id, string Name, string OriginalName, string AccountType, string InferredAccountType, bool IncludeInAnalytics, bool? IncludeInAnalyticsOverride, bool IsProviderManaged, int PreferencesVersion);

    private sealed class AccountBankingClient : IFiskilBankingClient
    {
        public Task<IReadOnlyCollection<FiskilAccountData>> GetAccounts(string endUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FiskilAccountData>>([new("bank-account", null, null, "Renamed by bank", "Mortgage", "RESIDENTIAL_MORTGAGES", null, null, true, "OPEN", null, "{}")]);
        public Task<IReadOnlyCollection<FiskilBalanceData>> GetBalances(string endUserId, string? accountId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<FiskilBalanceData>>([]);
        public Task<IReadOnlyCollection<FiskilTransactionData>> GetTransactions(string endUserId, string? accountId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<FiskilTransactionData>>([]);
    }
}
