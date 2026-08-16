using System.Net;
using Microsoft.Extensions.Options;
using Svix;

namespace Finyte.Api.Tenancy;

public interface IClerkWebhookVerifier
{
    string Verify(string payload, IHeaderDictionary headers);
}

public sealed class ClerkWebhookVerifier(IOptions<ClerkWebhookOptions> options) : IClerkWebhookVerifier
{
    public string Verify(string payload, IHeaderDictionary headers)
    {
        var signingSecret = options.Value.WebhookSigningSecret;
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            throw new ClerkWebhookConfigurationException();
        }

        var messageId = GetRequiredHeader(headers, "svix-id");
        var webhookHeaders = new WebHeaderCollection
        {
            { "svix-id", messageId },
            { "svix-timestamp", GetRequiredHeader(headers, "svix-timestamp") },
            { "svix-signature", GetRequiredHeader(headers, "svix-signature") }
        };

        new Webhook(signingSecret).Verify(payload, webhookHeaders);
        return messageId;
    }

    private static string GetRequiredHeader(IHeaderDictionary headers, string name)
    {
        var value = headers[name].ToString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new ClerkWebhookVerificationException($"Missing {name} header.")
            : value;
    }
}

public sealed class ClerkWebhookConfigurationException : InvalidOperationException
{
    public ClerkWebhookConfigurationException() : base("Clerk webhook signing secret is not configured.")
    {
    }
}

public sealed class ClerkWebhookVerificationException(string message) : InvalidOperationException(message);
