using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;

namespace Finyte.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", GetCurrentUser).RequireAuthorization().WithName("GetCurrentUser");

        return app;
    }

    private static Ok<CurrentUserResponse> GetCurrentUser(ClaimsPrincipal user)
    {
        var userId = EndpointUser.GetUserId(user);

        return TypedResults.Ok(new CurrentUserResponse(userId));
    }

    private sealed record CurrentUserResponse(string UserId);
}
