namespace Finyte.Data.Billing;

public interface IBillingAccess
{
    Task<BillingAccessState> GetAccess(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> HasAccess(Guid tenantId, CancellationToken cancellationToken);
}
