using Finyte.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Endpoints;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").WithName("GetHealth");

        app.MapGet("/api/app/status", GetAppStatus).WithName("GetAppStatus");

        return app;
    }

    private static async Task<Ok<AppStatusResponse>> GetAppStatus(
        FinyteDbContext dbContext,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        return TypedResults.Ok(new AppStatusResponse(
            "Finyte.Api",
            environment.EnvironmentName,
            canConnect,
            DateTimeOffset.UtcNow));
    }
}
