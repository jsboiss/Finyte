using Finyte.Api.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Finyte.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", GetCurrentUser).RequireAuthorization().WithName("GetCurrentUser");

        return app;
    }

    private static async Task<Ok<CurrentUserResponse>> GetCurrentUser(
        TenantResolver tenantResolver,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var currentTenant = await tenantResolver.Resolve(httpContext.User, cancellationToken);
        var onboarding = new OnboardingState(HasTenant: true);

        return TypedResults.Ok(new CurrentUserResponse(currentTenant.UserId, currentTenant.TenantId, currentTenant.Role.ToString(), onboarding));
    }

    private sealed record CurrentUserResponse(string UserId, Guid TenantId, string Role, OnboardingState Onboarding);

    private sealed record OnboardingState(bool HasTenant);
}
