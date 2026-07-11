using Finyte.Api.Tenancy;
using Svix.Exceptions;

namespace Finyte.Api.Endpoints;

public static class ClerkWebhookEndpoints
{
    public static IEndpointRouteBuilder MapClerkWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/clerk", HandleWebhook)
            .AllowAnonymous()
            .WithName("HandleClerkWebhook");

        return app;
    }

    private static async Task<IResult> HandleWebhook(
        HttpRequest request,
        IClerkWebhookVerifier verifier,
        IClerkWebhookIngestor ingestor,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var messageId = verifier.Verify(payload, request.Headers);
            await ingestor.Ingest(messageId, payload, cancellationToken);
            return TypedResults.Ok();
        }
        catch (ClerkWebhookConfigurationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (WebhookVerificationException)
        {
            return TypedResults.BadRequest("Invalid Clerk webhook signature.");
        }
        catch (ClerkWebhookVerificationException exception)
        {
            return TypedResults.BadRequest(exception.Message);
        }
    }
}
