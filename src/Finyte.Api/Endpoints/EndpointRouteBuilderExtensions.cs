using Finyte.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;

namespace Finyte.Api.Endpoints;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app, bool authEnabled)
    {
        app.MapHealthChecks("/health").WithName("GetHealth");

        app.MapGet("/api/app/status", GetAppStatus).WithName("GetAppStatus");

        if (authEnabled)
        {
            app.MapGet("/api/auth/me", GetCurrentUser).RequireAuthorization().WithName("GetCurrentUser");
        }

        return app;
    }

    private static async Task<Ok<AppStatusResponse>> GetAppStatus(
        FinyteDbContext dbContext,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        var response = new AppStatusResponse("Finyte.Api", environment.EnvironmentName, canConnect, DateTimeOffset.UtcNow);

        return TypedResults.Ok(response);
    }

    private static Ok<CurrentUserResponse> GetCurrentUser(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? string.Empty;

        return TypedResults.Ok(new CurrentUserResponse(userId));
    }
}
