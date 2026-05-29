namespace Finyte.Api.Endpoints;

public static class RouteBuilder
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app, bool authEnabled)
    {
        app.MapHealthChecks("/health").WithName("GetHealth");
        app.MapAppEndpoints();

        if (authEnabled)
        {
            app.MapAuthEndpoints();
            app.MapBankingAccountEndpoints();
        }

        return app;
    }
}
