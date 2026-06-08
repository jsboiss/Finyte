using Finyte.Core.Billing;
using Finyte.Data;
using Finyte.Data.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class BillingAccessTests
{
    [Fact]
    public async Task ActiveSubscriptionGrantsAccess()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(tenantId, "active"));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        var access = await billingAccess.GetAccess(tenantId, CancellationToken.None);

        Assert.True(access.HasAccess);
        Assert.Equal("active", access.Status);
    }

    [Fact]
    public async Task TrialingSubscriptionGrantsAccess()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(tenantId, "trialing"));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        Assert.True(await billingAccess.HasAccess(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveSubscriptionScheduledToCancelGrantsAccessUntilCurrentPeriodEnds()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "active",
            currentPeriodEnd: DateTimeOffset.UtcNow.AddDays(2),
            cancelAtPeriodEnd: true));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        var access = await billingAccess.GetAccess(tenantId, CancellationToken.None);

        Assert.True(access.HasAccess);
        Assert.True(access.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task ActiveSubscriptionScheduledToCancelDoesNotGrantAccessAfterCurrentPeriodEnds()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "active",
            currentPeriodEnd: DateTimeOffset.UtcNow.AddDays(-1),
            cancelAtPeriodEnd: true));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        Assert.False(await billingAccess.HasAccess(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task PastDueSubscriptionGrantsAccessUntilCurrentPeriodEnds()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "past_due",
            currentPeriodEnd: DateTimeOffset.UtcNow.AddDays(2)));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        Assert.True(await billingAccess.HasAccess(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredPastDueSubscriptionDoesNotGrantAccess()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "past_due",
            currentPeriodEnd: DateTimeOffset.UtcNow.AddDays(-1)));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        Assert.False(await billingAccess.HasAccess(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task AccessIsTenantScoped()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(otherTenantId, "active"));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        Assert.False(await billingAccess.HasAccess(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task AccessibleSubscriptionIsPreferredOverNewerInactiveSubscription()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "active",
            updatedAt: DateTimeOffset.UtcNow.AddDays(-1)));
        dbContext.BillingSubscriptions.Add(CreateSubscription(
            tenantId,
            "canceled",
            updatedAt: DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        var billingAccess = new BillingAccess(dbContext, TimeProvider.System);

        var access = await billingAccess.GetAccess(tenantId, CancellationToken.None);

        Assert.True(access.HasAccess);
        Assert.Equal("active", access.Status);
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }

    private static BillingSubscription CreateSubscription(
        Guid tenantId,
        string status,
        DateTimeOffset? currentPeriodEnd = null,
        DateTimeOffset? updatedAt = null,
        bool cancelAtPeriodEnd = false)
    {
        var billingCustomer = new BillingCustomer
        {
            TenantId = tenantId,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var now = DateTimeOffset.UtcNow;

        return new BillingSubscription
        {
            TenantId = tenantId,
            BillingCustomer = billingCustomer,
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            StripeCustomerId = billingCustomer.StripeCustomerId,
            StripePriceId = "price_test",
            Status = status,
            CurrentPeriodStart = now.AddDays(-28),
            CurrentPeriodEnd = currentPeriodEnd ?? now.AddDays(2),
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now
        };
    }
}
