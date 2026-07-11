using System.Security.Cryptography;
using System.Text;
using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;

namespace Finyte.Api.ProviderSync;

public interface IFiskilWebhookVerifier
{
    bool Verify(string payload, string signature);
}

public sealed class FiskilWebhookVerifier(IOptions<FiskilOptions> options) : IFiskilWebhookVerifier
{
    public bool Verify(string payload, string signature)
    {
        var webhookSecret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            throw new FiskilWebhookConfigurationException();
        }

        try
        {
            var secret = Convert.FromBase64String(webhookSecret);
            var expectedSignature = Convert.FromBase64String(signature);
            var actualSignature = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(payload));
            return CryptographicOperations.FixedTimeEquals(actualSignature, expectedSignature);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class FiskilWebhookConfigurationException : InvalidOperationException
{
    public FiskilWebhookConfigurationException() : base("Fiskil webhook signing secret is not configured.")
    {
    }
}
