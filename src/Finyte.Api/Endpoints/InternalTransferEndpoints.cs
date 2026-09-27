using Finyte.Api.Tenancy;
using Finyte.Data.Tenancy;
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
        group.MapPost("/reclassify", Reclassify).WithName("ReclassifyInternalTransfers");
        return app;
    }

    private static async Task<IResult> GetReview(string? view, DateOnly? from, DateOnly? to, int? page,
        TenantResolver tenantResolver, HttpContext httpContext, IBillingAccess billingAccess,
        InternalTransferService service, TenantCalendars calendars, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var calendar = await calendars.For(tenant.TenantId, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to review transfers.", statusCode: 402);
        }
        var end = to ?? calendar.Today;
        if (end < DateOnly.MinValue.AddDays(93) || end > DateOnly.MaxValue.AddDays(-4))
        {
            return Results.BadRequest("Choose a supported date range.");
        }
        var start = from ?? end.AddDays(-89);
        var reviewView = view ?? "transfers";
        if (!InternalTransferService.Views.Contains(reviewView) || start > end || end.DayNumber - start.DayNumber > 365
            || start < DateOnly.MinValue.AddDays(4) || end > DateOnly.MaxValue.AddDays(-4))
        {
            return Results.BadRequest("Choose transfers or excluded and a date range of up to 366 days.");
        }
        return Results.Ok(await service.GetReview(tenant.TenantId, reviewView, start, end, Math.Clamp(page ?? 1, 1, 100000), cancellationToken));
    }

    private static async Task<IResult> Review(TransferDecisionRequest request, TenantResolver tenantResolver,
        HttpContext httpContext, IBillingAccess billingAccess, InternalTransferService service, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to review transfers.", statusCode: 402);
        }
        if (request.Action is not ("mark" or "exclude" or "reset") || (request.Action == "mark" && request.CounterpartyAccountId is null))
        {
            return Results.BadRequest("Mark a transaction with the other account, exclude it, or reset it.");
        }
        try
        {
            await service.Review(tenant.TenantId, request, cancellationToken);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict("Another update changed this transaction. Refresh and try again.");
        }
    }

    private static async Task<IResult> Reclassify(TenantResolver tenantResolver, HttpContext httpContext, IBillingAccess billingAccess,
        InternalTransferService service, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        if (!await billingAccess.HasAccess(tenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to review transfers.", statusCode: 402);
        }
        return Results.Ok(new { changed = await service.Reclassify(tenant.TenantId, cancellationToken) });
    }
}
