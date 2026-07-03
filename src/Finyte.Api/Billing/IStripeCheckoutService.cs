using Finyte.Api.Tenancy;

namespace Finyte.Api.Billing;

public interface IStripeCheckoutService
{
    Task<CheckoutSessionResponse> CreateCheckoutSession(
        CheckoutSessionRequest request,
        CurrentTenant currentTenant,
        CancellationToken cancellationToken);
}
