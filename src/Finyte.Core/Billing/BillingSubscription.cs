using Finyte.Core.Tenancy;

namespace Finyte.Core.Billing;

public sealed class BillingSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid BillingCustomerId { get; set; }
    public BillingCustomer? BillingCustomer { get; set; }
    public required string StripeSubscriptionId { get; set; }
    public required string StripeCustomerId { get; set; }
    public required string StripePriceId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset? TrialEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
