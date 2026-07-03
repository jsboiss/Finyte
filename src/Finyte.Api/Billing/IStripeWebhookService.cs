namespace Finyte.Api.Billing;

public interface IStripeWebhookService
{
    Task HandleWebhook(string payload, string signatureHeader, CancellationToken cancellationToken);
}
