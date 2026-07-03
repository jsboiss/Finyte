using Finyte.Api.Tenancy;

namespace Finyte.Api.Billing;

public interface IStripePortalService
{
    Task<PortalSessionResponse> CreatePortalSession(CurrentTenant currentTenant, CancellationToken cancellationToken);
}
