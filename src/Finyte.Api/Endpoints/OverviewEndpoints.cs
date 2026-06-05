using Finyte.Api.Tenancy;
using Finyte.Core.Analytics;
using Finyte.Data.Analytics;

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

    private static async Task<OverviewResponse> GetOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        return await projectionDispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, GetCurrentMonthKey()),
            cancellationToken);
    }

    private static async Task<OverviewResponse> RefreshOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        return await projectionDispatcher.RebuildOverview(
            new OverviewProjectionScope(currentTenant.TenantId, accountId, GetCurrentMonthKey()),
            cancellationToken);
    }

    private static async Task<IResult> RepairOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IWebHostEnvironment environment,
        IProjectionDispatcher projectionDispatcher,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Results.NotFound();
        }

        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
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
