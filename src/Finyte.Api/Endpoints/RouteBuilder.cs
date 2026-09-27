namespace Finyte.Api.Endpoints;

public static class RouteBuilder
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").WithName("GetHealth");
        app.MapAppEndpoints();
        app.MapAuthEndpoints();
        app.MapFamilyEndpoints();
        app.MapClerkWebhookEndpoints();
        app.MapBillingEndpoints();
        app.MapFiskilWebhookEndpoints();
        app.MapProviderConnectionEndpoints();
        app.MapBankingAccountEndpoints();
        app.MapAccountGroupEndpoints();
        app.MapImportEndpoints();
        app.MapInternalTransferEndpoints();
        app.MapTransactionEndpoints();
        app.MapOverviewEndpoints();
        app.MapCashFlowEndpoints();
        app.MapPayCycleEndpoints();
        app.MapBudgetEndpoints();
        app.MapRecurringPaymentEndpoints();

        return app;
    }
}
