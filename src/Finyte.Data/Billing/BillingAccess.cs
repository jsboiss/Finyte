using Finyte.Core.Billing;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Billing;

public sealed class BillingAccess(FinyteDbContext dbContext, TimeProvider timeProvider) : IBillingAccess
{
    public async Task<BillingAccessState> GetAccess(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var subscriptions = await dbContext.BillingSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var subscription = subscriptions
            .OrderByDescending(x => HasAccess(x, now))
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

        if (subscription is null)
        {
            return new BillingAccessState(
                HasAccess: false,
                Status: null,
                StripePriceId: null,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: false);
        }

        return new BillingAccessState(
            HasAccess(subscription, now),
            subscription.Status,
            subscription.StripePriceId,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd);
    }

    public async Task<bool> HasAccess(Guid tenantId, CancellationToken cancellationToken)
    {
        var access = await GetAccess(tenantId, cancellationToken);
        return access.HasAccess;
    }

    private static bool HasAccess(BillingSubscription subscription, DateTimeOffset now)
    {
        return subscription.Status switch
        {
            "active" => !HasExpiredScheduledCancellation(subscription, now),
            "trialing" => !HasExpiredScheduledCancellation(subscription, now),
            "past_due" => subscription.CurrentPeriodEnd is not null && subscription.CurrentPeriodEnd >= now,
            _ => false
        };
    }

    private static bool HasExpiredScheduledCancellation(BillingSubscription subscription, DateTimeOffset now)
    {
        return subscription.CancelAtPeriodEnd
            && subscription.CurrentPeriodEnd is not null
            && subscription.CurrentPeriodEnd < now;
    }
}
