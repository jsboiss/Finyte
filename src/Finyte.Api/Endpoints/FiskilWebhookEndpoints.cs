using System.Text.Json;
using Finyte.Api.ProviderSync;

namespace Finyte.Api.Endpoints;

public static class FiskilWebhookEndpoints
{
    public static IEndpointRouteBuilder MapFiskilWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/provider-sync/fiskil/webhook", HandleWebhook)
            .AllowAnonymous()
            .WithName("HandleFiskilWebhook");

        return app;
    }

    private static async Task<IResult> HandleWebhook(
        HttpRequest httpRequest,
        IFiskilWebhookIngestor webhookIngestor,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(httpRequest.Body);
        var payloadJson = await reader.ReadToEndAsync(cancellationToken);
        var request = JsonSerializer.Deserialize<FiskilWebhookRequest>(payloadJson);

        if (request is null || string.IsNullOrWhiteSpace(request.MessageId) || string.IsNullOrWhiteSpace(request.Data.Event))
        {
            return Results.BadRequest();
        }

        var result = await webhookIngestor.Ingest(request, payloadJson, cancellationToken);
        return Results.Ok(result);
    }
}
