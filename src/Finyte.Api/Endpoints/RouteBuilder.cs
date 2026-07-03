namespace Finyte.Api.Endpoints;

public static class RouteBuilder
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").WithName("GetHealth");
        app.MapAppEndpoints();
        app.MapAuthEndpoints();
        app.MapBillingEndpoints();
        app.MapFiskilWebhookEndpoints();
        app.MapBankingAccountEndpoints();
        app.MapOverviewEndpoints();

        return app;
    }
}
