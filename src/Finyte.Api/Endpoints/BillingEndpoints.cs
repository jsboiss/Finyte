using Finyte.Api.Billing;
using Finyte.Api.Tenancy;
using Finyte.Data.Billing;
using Microsoft.AspNetCore.Http.HttpResults;
using Stripe;

namespace Finyte.Api.Endpoints;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing").RequireAuthorization();

        group.MapGet("/access", GetBillingAccess).WithName("GetBillingAccess");
        group.MapPost("/checkout-session", CreateCheckoutSession).WithName("CreateCheckoutSession");
        group.MapPost("/portal-session", CreatePortalSession).WithName("CreateBillingPortalSession");
        app.MapPost("/api/billing/webhook", HandleWebhook).AllowAnonymous().WithName("HandleBillingWebhook");

        return app;
    }

    private static async Task<Ok<BillingAccessResponse>> GetBillingAccess(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var access = await billingAccess.GetAccess(currentTenant.TenantId, cancellationToken);

        return TypedResults.Ok(new BillingAccessResponse(
            access.HasAccess,
            access.Status,
            access.StripePriceId,
            access.CurrentPeriodEnd,
            access.CancelAtPeriodEnd));
    }

    private static async Task<Results<Ok<CheckoutSessionResponse>, BadRequest<string>, ProblemHttpResult>> CreateCheckoutSession(
        CheckoutSessionRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IStripeCheckoutService checkoutService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Plan))
        {
            return TypedResults.BadRequest("Billing plan is required.");
        }

        try
        {
            var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
            var response = await checkoutService.CreateCheckoutSession(
                request with { Plan = request.Plan.Trim() },
                currentTenant,
                cancellationToken);

            return TypedResults.Ok(response);
        }
        catch (UnknownBillingPlanException)
        {
            return TypedResults.BadRequest("Billing plan is not available.");
        }
        catch (StripeConfigurationException)
        {
            return TypedResults.Problem("Stripe checkout is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (StripeException)
        {
            return TypedResults.Problem("Stripe checkout session could not be created.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<Results<Ok<PortalSessionResponse>, NotFound<string>, ProblemHttpResult>> CreatePortalSession(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IStripePortalService portalService,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
            var response = await portalService.CreatePortalSession(currentTenant, cancellationToken);

            return TypedResults.Ok(response);
        }
        catch (BillingCustomerNotFoundException)
        {
            return TypedResults.NotFound("Billing customer does not exist.");
        }
        catch (StripeConfigurationException)
        {
            return TypedResults.Problem("Stripe billing portal is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (StripeException)
        {
            return TypedResults.Problem("Stripe billing portal session could not be created.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem("Stripe billing portal session could not be created.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> HandleWebhook(
        HttpRequest request,
        IStripeWebhookService webhookService,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("Stripe-Signature", out var signatureHeader))
        {
            return TypedResults.BadRequest("Stripe signature is required.");
        }

        try
        {
            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync(cancellationToken);

            await webhookService.HandleWebhook(payload, signatureHeader.ToString(), cancellationToken);

            return TypedResults.Ok();
        }
        catch (StripeConfigurationException)
        {
            return TypedResults.Problem("Stripe webhook handling is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (StripeException)
        {
            return TypedResults.BadRequest("Stripe webhook signature or payload is invalid.");
        }
        catch (InvalidOperationException)
        {
            return TypedResults.Problem("Stripe webhook event could not be processed.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private sealed record BillingAccessResponse(
        bool HasAccess,
        string? Status,
        string? StripePriceId,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd);
}
