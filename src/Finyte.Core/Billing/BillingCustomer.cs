using Finyte.Core.Tenancy;

namespace Finyte.Core.Billing;

public sealed class BillingCustomer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public required string StripeCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<BillingSubscription> Subscriptions { get; set; } = [];
}
