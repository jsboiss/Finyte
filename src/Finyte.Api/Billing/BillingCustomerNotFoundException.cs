namespace Finyte.Api.Billing;

public sealed class BillingCustomerNotFoundException(Guid tenantId)
    : Exception($"Tenant '{tenantId}' does not have a Stripe billing customer.")
{
    public Guid TenantId { get; } = tenantId;
}
