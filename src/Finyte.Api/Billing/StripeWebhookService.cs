using Finyte.Core.Billing;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Finyte.Api.Billing;

public sealed class StripeWebhookService(
    IOptions<StripeOptions> stripeOptions,
    FinyteDbContext dbContext) : IStripeWebhookService
{
    public async Task HandleWebhook(string payload, string signatureHeader, CancellationToken cancellationToken)
    {
        var options = stripeOptions.Value;

        if (!options.HasWebhookConfiguration)
        {
            throw new StripeConfigurationException("Stripe webhook handling is not configured.");
        }

        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, options.WebhookSecret);
        var eventAlreadyProcessed = await dbContext.BillingEvents
            .AnyAsync(x => x.StripeEventId == stripeEvent.Id, cancellationToken);

        if (eventAlreadyProcessed)
        {
            return;
        }

        await ProcessEvent(stripeEvent, options.SecretKey, cancellationToken);

        dbContext.BillingEvents.Add(new BillingEvent
        {
            StripeEventId = stripeEvent.Id,
            Type = stripeEvent.Type,
            PayloadJson = payload,
            ProcessedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessEvent(Event stripeEvent, string secretKey, CancellationToken cancellationToken)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await ProcessCheckoutSessionCompleted(stripeEvent, secretKey, cancellationToken);
                break;

            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await ProcessSubscriptionEvent(stripeEvent, cancellationToken);
                break;

            case "invoice.paid":
            case "invoice.payment_failed":
                await ProcessInvoiceEvent(stripeEvent, secretKey, cancellationToken);
                break;
        }
    }

    private async Task ProcessCheckoutSessionCompleted(Event stripeEvent, string secretKey, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Session session || string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new StripeConfigurationException("Stripe secret key is required to retrieve checkout subscriptions.");
        }

        var subscriptionService = new SubscriptionService();
        var subscription = await subscriptionService.GetAsync(
            session.SubscriptionId,
            options: null,
            requestOptions: new RequestOptions { ApiKey = secretKey },
            cancellationToken: cancellationToken);
        var tenantId = TryParseTenantId(session.Metadata) ?? TryParseTenantId(session.ClientReferenceId);

        await UpsertSubscription(subscription, tenantId, cancellationToken);
    }

    private async Task ProcessSubscriptionEvent(Event stripeEvent, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
        {
            return;
        }

        await UpsertSubscription(subscription, TryParseTenantId(subscription.Metadata), cancellationToken);
    }

    private async Task ProcessInvoiceEvent(Event stripeEvent, string secretKey, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
        {
            return;
        }

        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new StripeConfigurationException("Stripe secret key is required to retrieve invoice subscriptions.");
        }

        var subscriptionService = new SubscriptionService();
        var subscription = await subscriptionService.GetAsync(
            subscriptionId,
            options: null,
            requestOptions: new RequestOptions { ApiKey = secretKey },
            cancellationToken: cancellationToken);

        await UpsertSubscription(subscription, TryParseTenantId(invoice.Parent?.SubscriptionDetails?.Metadata), cancellationToken);
    }

    private async Task UpsertSubscription(Subscription subscription, Guid? eventTenantId, CancellationToken cancellationToken)
    {
        var billingCustomer = await dbContext.BillingCustomers
            .SingleOrDefaultAsync(x => x.StripeCustomerId == subscription.CustomerId, cancellationToken);

        if (billingCustomer is null)
        {
            if (eventTenantId is null)
            {
                throw new InvalidOperationException("Stripe subscription event could not be mapped to a tenant.");
            }

            billingCustomer = new BillingCustomer
            {
                TenantId = eventTenantId.Value,
                StripeCustomerId = subscription.CustomerId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.BillingCustomers.Add(billingCustomer);
        }

        var tenantId = eventTenantId ?? billingCustomer.TenantId;
        var subscriptionItem = subscription.Items?.Data.FirstOrDefault();
        var priceId = subscriptionItem?.Price?.Id;

        if (string.IsNullOrWhiteSpace(priceId))
        {
            throw new InvalidOperationException("Stripe subscription event did not include a price id.");
        }

        var existingSubscription = await dbContext.BillingSubscriptions
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existingSubscription is null)
        {
            dbContext.BillingSubscriptions.Add(new BillingSubscription
            {
                TenantId = tenantId,
                BillingCustomer = billingCustomer,
                StripeSubscriptionId = subscription.Id,
                StripeCustomerId = subscription.CustomerId,
                StripePriceId = priceId,
                Status = subscription.Status,
                CurrentPeriodStart = ToDateTimeOffset(subscriptionItem?.CurrentPeriodStart),
                CurrentPeriodEnd = ToDateTimeOffset(subscriptionItem?.CurrentPeriodEnd),
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                TrialEnd = ToDateTimeOffset(subscription.TrialEnd),
                CreatedAt = now,
                UpdatedAt = now
            });

            return;
        }

        existingSubscription.TenantId = tenantId;
        existingSubscription.BillingCustomer = billingCustomer;
        existingSubscription.StripeCustomerId = subscription.CustomerId;
        existingSubscription.StripePriceId = priceId;
        existingSubscription.Status = subscription.Status;
        existingSubscription.CurrentPeriodStart = ToDateTimeOffset(subscriptionItem?.CurrentPeriodStart);
        existingSubscription.CurrentPeriodEnd = ToDateTimeOffset(subscriptionItem?.CurrentPeriodEnd);
        existingSubscription.CancelAtPeriodEnd = subscription.CancelAtPeriodEnd;
        existingSubscription.TrialEnd = ToDateTimeOffset(subscription.TrialEnd);
        existingSubscription.UpdatedAt = now;
    }

    private static Guid? TryParseTenantId(IReadOnlyDictionary<string, string>? metadata)
    {
        return metadata is not null && metadata.TryGetValue("tenantId", out var tenantId)
            ? TryParseTenantId(tenantId)
            : null;
    }

    private static Guid? TryParseTenantId(string? tenantId)
    {
        return Guid.TryParse(tenantId, out var parsedTenantId) ? parsedTenantId : null;
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
    }
}
