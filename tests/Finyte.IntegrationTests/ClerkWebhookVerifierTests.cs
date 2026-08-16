using Finyte.Api.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Svix;
using Svix.Exceptions;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ClerkWebhookVerifierTests
{
    [Fact]
    public void ValidSignatureReturnsMessageId()
    {
        const string secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
        const string messageId = "message-1";
        const string payload = "{\"type\":\"organization.created\"}";
        var timestamp = DateTimeOffset.UtcNow;
        var signature = new Webhook(secret).Sign(messageId, timestamp, payload);
        var headers = new HeaderDictionary
        {
            ["svix-id"] = messageId,
            ["svix-timestamp"] = timestamp.ToUnixTimeSeconds().ToString(),
            ["svix-signature"] = signature
        };
        var verifier = new ClerkWebhookVerifier(Options.Create(new ClerkWebhookOptions { WebhookSigningSecret = secret }));

        var verifiedMessageId = verifier.Verify(payload, headers);

        Assert.Equal(messageId, verifiedMessageId);
    }

    [Fact]
    public void InvalidSignatureIsRejected()
    {
        var headers = new HeaderDictionary
        {
            ["svix-id"] = "message-1",
            ["svix-timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["svix-signature"] = "v1,invalid"
        };
        var verifier = new ClerkWebhookVerifier(Options.Create(new ClerkWebhookOptions
        {
            WebhookSigningSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw"
        }));

        Assert.Throws<WebhookVerificationException>(() => verifier.Verify("{}", headers));
    }
}
