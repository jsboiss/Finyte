using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class CashFlowApiTests
{
    [Fact]
    public async Task CashFlowReturnsEveryDayInRequestedRange()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var tenantId = await ResolveTenantId(client);
        await Seed(factory, tenantId);

        var response = await client.GetFromJsonAsync<CashFlowRangeResponse>("/api/cash-flow?from=2026-06-01&to=2026-06-07");

        Assert.NotNull(response);
        Assert.Equal("2026-06-01", response.From);
        Assert.Equal("2026-06-07", response.To);
        Assert.Equal(7, response.DailyCashFlow.Count);
        Assert.Equal(12500, response.DailyCashFlow[1].IncomeMinorUnits);
        Assert.Equal(4200, response.DailyCashFlow[2].ExpenseMinorUnits);
    }

    [Fact]
    public async Task CashFlowRejectsAnInvertedRange()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var tenantId = await ResolveTenantId(client);
        await AddActiveSubscription(factory, tenantId);

        var response = await client.GetAsync("/api/cash-flow?from=2026-06-07&to=2026-06-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Guid> ResolveTenantId(HttpClient client)
    {
        var currentUser = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.NotNull(currentUser);
        return currentUser.TenantId;
    }

    private static async Task Seed(FinyteApiFactory factory, Guid tenantId)
    {
        await AddActiveSubscription(factory, tenantId);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var account = new Account
        {
            TenantId = tenantId,
            Name = "Everyday",
            Currency = "AUD",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Accounts.Add(account);
        dbContext.Transactions.AddRange(
            new Transaction
            {
                TenantId = tenantId,
                Account = account,
                FiskilTransactionId = "cash-flow-income",
                Amount = 125m,
                Currency = "AUD",
                Status = "posted",
                PostedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Transaction
            {
                TenantId = tenantId,
                Account = account,
                FiskilTransactionId = "cash-flow-expense",
                Amount = -42m,
                Currency = "AUD",
                Status = "posted",
                PostedAt = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
                CreatedAt = DateTimeOffset.UtcNow
            });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddActiveSubscription(FinyteApiFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var now = DateTimeOffset.UtcNow;
        var billingCustomer = new BillingCustomer
        {
            TenantId = tenantId,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            CreatedAt = now
        };
        dbContext.BillingCustomers.Add(billingCustomer);
        dbContext.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId,
            BillingCustomer = billingCustomer,
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            StripeCustomerId = billingCustomer.StripeCustomerId,
            StripePriceId = "price_test",
            Status = "active",
            CurrentPeriodStart = now.AddDays(-10),
            CurrentPeriodEnd = now.AddDays(20),
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
    }

    private sealed record CurrentUserResponse(string UserId, Guid TenantId, string Role);
}
