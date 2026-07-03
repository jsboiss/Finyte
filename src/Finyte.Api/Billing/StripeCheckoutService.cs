using Finyte.Api.Tenancy;
using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Finyte.Api.Billing;

public sealed class StripeCheckoutService(
    IOptions<StripeOptions> stripeOptions,
    FinyteDbContext dbContext) : IStripeCheckoutService
{
    public async Task<CheckoutSessionResponse> CreateCheckoutSession(
        CheckoutSessionRequest request,
        CurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        var options = stripeOptions.Value;

        if (!options.HasCheckoutConfiguration)
        {
            throw new StripeConfigurationException("Stripe checkout is not configured.");
        }

        if (!options.TryGetPriceId(request.Plan, out var priceId))
        {
            throw new UnknownBillingPlanException(request.Plan);
        }

        var billingCustomer = await GetOrCreateBillingCustomer(currentTenant.TenantId, options.SecretKey, cancellationToken);
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = currentTenant.TenantId.ToString(),
            ["userId"] = currentTenant.UserId,
            ["plan"] = request.Plan
        };

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = billingCustomer.StripeCustomerId,
            SuccessUrl = options.SuccessUrl,
            CancelUrl = options.CancelUrl,
            ClientReferenceId = currentTenant.TenantId.ToString(),
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = metadata
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ]
        };

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(
            sessionOptions,
            new RequestOptions { ApiKey = options.SecretKey },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException("Stripe did not return a Checkout Session URL.");
        }

        return new CheckoutSessionResponse(session.Url);
    }

    private async Task<BillingCustomer> GetOrCreateBillingCustomer(Guid tenantId, string secretKey, CancellationToken cancellationToken)
    {
        var existingCustomer = await dbContext.BillingCustomers
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (existingCustomer is not null)
        {
            return existingCustomer;
        }

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleAsync(x => x.Id == tenantId, cancellationToken);
        var customerService = new CustomerService();
        var customer = await customerService.CreateAsync(
            new CustomerCreateOptions
            {
                Name = tenant.Name,
                Metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId.ToString()
                }
            },
            new RequestOptions { ApiKey = secretKey },
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var billingCustomer = new BillingCustomer
        {
            TenantId = tenantId,
            StripeCustomerId = customer.Id,
            CreatedAt = now
        };

        dbContext.BillingCustomers.Add(billingCustomer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return billingCustomer;
    }
}
