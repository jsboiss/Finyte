using System.Net;
using System.Net.Http.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class BillingGateApiTests
{
    [Fact]
    public async Task BillingAccessReportsNoAccessWithoutSubscription()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<BillingAccessResponse>("/api/billing/access");

        Assert.NotNull(response);
        Assert.False(response.HasAccess);
        Assert.Null(response.Status);
    }

    [Fact]
    public async Task OverviewReturnsPaymentRequiredWithoutBillingAccess()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/overview");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task AccountCreationReturnsPaymentRequiredWithoutBillingAccess()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Everyday",
            currentBalance = 100m,
            availableBalance = 100m,
            currency = "AUD"
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task OverviewWorksWithBillingAccess()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var tenantId = await ResolveTenantId(client);
        await AddActiveSubscription(factory, tenantId);

        var response = await client.GetAsync("/api/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AccountCreationWorksWithBillingAccess()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        var tenantId = await ResolveTenantId(client);
        await AddActiveSubscription(factory, tenantId);

        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Everyday",
            currentBalance = 100m,
            availableBalance = 100m,
            currency = "AUD"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OtherTenantSubscriptionDoesNotUnlockCurrentTenantOverview()
    {
        await using var factory = new FinyteApiFactory();
        var client = factory.CreateClient();
        await ResolveTenantId(client);
        await AddActiveSubscription(factory, Guid.NewGuid());

        var response = await client.GetAsync("/api/overview");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    private static async Task<Guid> ResolveTenantId(HttpClient client)
    {
        var currentUser = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");

        Assert.NotNull(currentUser);
        return currentUser.TenantId;
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

    private sealed record BillingAccessResponse(
        bool HasAccess,
        string? Status,
        string? StripePriceId,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd);

    private sealed record CurrentUserResponse(string UserId, Guid TenantId, string Role);
}
