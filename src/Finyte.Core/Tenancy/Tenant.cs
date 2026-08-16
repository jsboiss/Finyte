using Finyte.Core.Billing;

namespace Finyte.Core.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ClerkOrganizationId { get; set; }
    public required string Name { get; set; }
    public long FinancialDataVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TenantMember> Members { get; set; } = [];
    public BillingCustomer? BillingCustomer { get; set; }
    public ICollection<BillingSubscription> BillingSubscriptions { get; set; } = [];
}
