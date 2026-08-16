using Finyte.Api.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Finyte.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireAuthorization();

        group.MapGet("/me", GetCurrentUser).WithName("GetCurrentUser");
        group.MapPost("/family", ProvisionFamily).WithName("ProvisionFamily");

        return app;
    }

    private static async Task<Ok<CurrentUserResponse>> GetCurrentUser(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.TryResolve(httpContext.User, cancellationToken);

        return TypedResults.Ok(ToResponse(currentTenant));
    }

    private static async Task<Results<Ok<CurrentUserResponse>, BadRequest<string>>> ProvisionFamily(
        ProvisionFamilyRequest request,
        TenantResolver tenantResolver,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.BadRequest("Family name is required.");
        }

        var currentTenant = await tenantResolver.Provision(httpContext.User, request.Name.Trim(), cancellationToken);

        return TypedResults.Ok(ToResponse(currentTenant));
    }

    private static CurrentUserResponse ToResponse(CurrentTenant? currentTenant)
    {
        return new CurrentUserResponse(
            currentTenant?.UserId,
            currentTenant?.TenantId,
            currentTenant?.Role.ToString(),
            new OnboardingState(currentTenant is not null));
    }

    private sealed record ProvisionFamilyRequest(string Name);

    private sealed record CurrentUserResponse(string? UserId, Guid? TenantId, string? Role, OnboardingState Onboarding);

    private sealed record OnboardingState(bool HasFamily);
}
