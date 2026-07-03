using System.Text.Json;
using Finyte.Api.ProviderSync;
using Finyte.Data.ProviderSync;

namespace Finyte.Api.Endpoints;

public static class FiskilWebhookEndpoints
{
    public static IEndpointRouteBuilder MapFiskilWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/provider-sync/fiskil/webhook", HandleWebhook)
            .AllowAnonymous()
            .WithName("HandleFiskilWebhook");
        app.MapPost("/api/provider-sync/runs/{syncRunId:guid}/run", RunSync)
            .RequireAuthorization()
            .WithName("RunProviderSync");

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

    private static async Task<IResult> RunSync(
        Guid syncRunId,
        IWebHostEnvironment environment,
        IProviderSyncRunner providerSyncRunner,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Results.NotFound();
        }

        try
        {
            await providerSyncRunner.Run(syncRunId, cancellationToken);
            return Results.Ok();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(exception.Message);
        }
    }
}
