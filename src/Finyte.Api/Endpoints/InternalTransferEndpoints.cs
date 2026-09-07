using Finyte.Api.Tenancy;
using Finyte.Data.Billing;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class InternalTransferEndpoints
{
    public static IEndpointRouteBuilder MapInternalTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/internal-transfers").RequireAuthorization();
        group.MapGet("/", GetReview).WithName("GetInternalTransferReview");
        group.MapPost("/review", Review).WithName("ReviewInternalTransfer");
        return app;
    }

    private static async Task<IResult> GetReview(string? status, DateOnly? from, DateOnly? to, int? page,
        TenantResolver tenantResolver, HttpContext httpContext, IBillingAccess billingAccess,
        InternalTransferService service, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to review transfers.", statusCode: 402);
        }
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (end < DateOnly.MinValue.AddDays(93) || end > DateOnly.MaxValue.AddDays(-4))
        {
            return Results.BadRequest("Choose a supported date range.");
        }
        var start = from ?? end.AddDays(-89);
        var reviewStatus = status ?? "suggested";
        if (reviewStatus is not ("suggested" or "confirmed" or "dismissed" or "needs-review")
            || start > end || end.DayNumber - start.DayNumber > 365
            || start < DateOnly.MinValue.AddDays(4) || end > DateOnly.MaxValue.AddDays(-4))
        {
            return Results.BadRequest("Choose a valid review status and a date range of up to 366 days.");
        }
        return Results.Ok(await service.GetReview(tenant.TenantId, reviewStatus, start, end, Math.Clamp(page ?? 1, 1, 100000), cancellationToken));
    }

    private static async Task<IResult> Review(TransferDecisionRequest request, TenantResolver tenantResolver,
        HttpContext httpContext, IBillingAccess billingAccess, InternalTransferService service, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to review transfers.", statusCode: 402);
        }
        if (request.Action is not ("confirm" or "dismiss" or "reset") || request.DebitTransactionId == request.CreditTransactionId)
        {
            return Results.BadRequest("Choose two different transactions and confirm, dismiss, or reset the pair.");
        }
        try
        {
            await service.Review(tenant.TenantId, tenant.UserId, request, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(exception.Message);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict("Another update changed these transactions. Refresh and try again.");
        }
    }
}
