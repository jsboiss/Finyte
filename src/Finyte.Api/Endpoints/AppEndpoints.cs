using Finyte.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Finyte.Api.Endpoints;

public static class AppEndpoints
{
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/status", GetAppStatus).WithName("GetAppStatus");

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

    private sealed record AppStatusResponse(string Name, string Environment, bool DatabaseAvailable, DateTimeOffset ServerTime);
}
