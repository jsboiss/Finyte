using Finyte.Api.Tenancy;
using Finyte.Core.Analytics;
using Finyte.Data.Tenancy;
using Finyte.Data;
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
        bool? includeInternalTransfers,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        IProjectionDispatcher projectionDispatcher,
        FinyteDbContext dbContext,
        TenantCalendars calendars,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var calendar = await calendars.For(currentTenant.TenantId, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to view the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        if (includeInternalTransfers == true)
        {
            return Results.Ok(await new OverviewProjector(dbContext, calendars).ReadIncludingTransfers(
                new OverviewProjectionScope(currentTenant.TenantId, accountId, calendar.CurrentMonthKey), cancellationToken));
        }
        var response = await projectionDispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, calendar.CurrentMonthKey),
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> RefreshOverview(
        Guid? accountId,
        bool? includeInternalTransfers,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IBillingAccess billingAccess,
        IProjectionInvalidator projectionInvalidator,
        IProjectionDispatcher projectionDispatcher,
        FinyteDbContext dbContext,
        TenantCalendars calendars,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var calendar = await calendars.For(currentTenant.TenantId, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to refresh the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var monthKey = calendar.CurrentMonthKey;
        if (includeInternalTransfers == true)
        {
            return Results.Ok(await new OverviewProjector(dbContext, calendars).ReadIncludingTransfers(
                new OverviewProjectionScope(currentTenant.TenantId, accountId, monthKey), cancellationToken));
        }
        await projectionInvalidator.OverviewRequested(currentTenant.TenantId, accountId, monthKey, cancellationToken);
        var response = await projectionDispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, monthKey), cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> RepairOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IBillingAccess billingAccess,
        IProjectionDispatcher projectionDispatcher,
        TenantCalendars calendars,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Results.NotFound();
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var calendar = await calendars.For(currentTenant.TenantId, cancellationToken);

        if (!await billingAccess.HasAccess(currentTenant.TenantId, cancellationToken))
        {
            return Results.Problem("An active subscription is required to repair the financial overview.", statusCode: StatusCodes.Status402PaymentRequired);
        }

        var response = await projectionDispatcher.RebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, calendar.CurrentMonthKey),
            cancellationToken);

        return Results.Ok(response);
    }
}
