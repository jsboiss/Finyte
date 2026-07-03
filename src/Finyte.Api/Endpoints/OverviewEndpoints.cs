using Finyte.Api.Tenancy;
using Finyte.Core.Analytics;
using Finyte.Data.Analytics;
using Finyte.Data.Billing;

namespace Finyte.Api.Endpoints;

public static class OverviewEndpoints
{
    public static IEndpointRouteBuilder MapOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/overview").RequireAuthorization();

        group.MapGet("/", GetOverview).WithName("GetOverview");
        group.MapPost("/refresh", RefreshOverview).WithName("RefreshOverview");
        group.MapPost("/repair", RepairOverview).WithName("RepairOverview");

        return app;
    }

    private static async Task<IResult> GetOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to view the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var response = await projectionDispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, GetCurrentMonthKey()),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> RefreshOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to refresh the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var response = await projectionDispatcher.RebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, GetCurrentMonthKey()),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> RepairOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IBillingAccess billingAccess,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Results.NotFound();
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to repair the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var response = await projectionDispatcher.RebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, GetCurrentMonthKey()),
            cancellationToken);

        return Results.Ok(response);
    }

    private static string GetCurrentMonthKey()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return $"{today.Year:D4}-{today.Month:D2}";
    }
}
