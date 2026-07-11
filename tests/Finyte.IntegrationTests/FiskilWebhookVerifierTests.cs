using System.Security.Cryptography;
using System.Text;
using Finyte.Api.ProviderSync;
using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class FiskilWebhookVerifierTests
{
    [Fact]
    public void ValidHmacSignatureIsAcceptedAndTamperedPayloadIsRejected()
    {
        const string payload = "{\"message_id\":\"message-1\"}";
        var secret = RandomNumberGenerator.GetBytes(32);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(payload)));
        var verifier = new FiskilWebhookVerifier(Options.Create(new FiskilOptions
        {
            WebhookSecret = Convert.ToBase64String(secret)
        }));

        Assert.True(verifier.Verify(payload, signature));
        Assert.False(verifier.Verify(payload + " ", signature));
    }
}
