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

        return app;
    }

    private static async Task<OverviewResponse> GetOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IOverviewProjector overviewProjector,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        return await overviewProjector.GetOrRebuild(currentTenant.TenantId, accountId, cancellationToken);
    }

    private static async Task<OverviewResponse> RefreshOverview(
        Guid? accountId,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        IOverviewProjector overviewProjector,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        return await overviewProjector.Rebuild(currentTenant.TenantId, accountId, cancellationToken);
    }
}
