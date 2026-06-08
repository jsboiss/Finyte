using Finyte.Api.Tenancy;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.BillingPortal;

namespace Finyte.Api.Billing;

public sealed class StripePortalService(
    IOptions<StripeOptions> stripeOptions,
    FinyteDbContext dbContext) : IStripePortalService
{
    public async Task<PortalSessionResponse> CreatePortalSession(CurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        var options = stripeOptions.Value;

        if (!options.HasPortalConfiguration)
        {
            throw new StripeConfigurationException("Stripe billing portal is not configured.");
        }

        var billingCustomer = await dbContext.BillingCustomers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == currentTenant.TenantId, cancellationToken);

        if (billingCustomer is null)
        {
            throw new BillingCustomerNotFoundException(currentTenant.TenantId);
        }

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(
            new SessionCreateOptions
            {
                Customer = billingCustomer.StripeCustomerId,
                ReturnUrl = options.PortalReturnUrl
            },
            new RequestOptions { ApiKey = options.SecretKey },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException("Stripe did not return a Billing Portal Session URL.");
        }

        return new PortalSessionResponse(session.Url);
    }
}
