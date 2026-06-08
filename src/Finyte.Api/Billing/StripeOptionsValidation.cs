namespace Finyte.Api.Billing;

public static class StripeOptionsValidation
{
    public static bool IsValid(StripeOptions x)
    {
        return IsValidSecretKey(x.SecretKey)
            && IsValidWebhookSecret(x.WebhookSecret)
            && x.Prices.All(y => IsValidPlanKey(y.Key) && IsValidPriceId(y.Value))
            && IsValidAbsoluteUrl(x.SuccessUrl)
            && IsValidAbsoluteUrl(x.CancelUrl)
            && IsValidAbsoluteUrl(x.PortalReturnUrl);
    }

    private static bool IsValidSecretKey(string x)
    {
        return string.IsNullOrWhiteSpace(x) || x.StartsWith("sk_", StringComparison.Ordinal);
    }

    private static bool IsValidWebhookSecret(string x)
    {
        return string.IsNullOrWhiteSpace(x) || x.StartsWith("whsec_", StringComparison.Ordinal);
    }

    private static bool IsValidPriceId(string x)
    {
        return string.IsNullOrWhiteSpace(x) || x.StartsWith("price_", StringComparison.Ordinal);
    }

    private static bool IsValidPlanKey(string x)
    {
        return !string.IsNullOrWhiteSpace(x)
            && x.All(y => char.IsAsciiLetterOrDigit(y) || y == '-' || y == '_');
    }

    private static bool IsValidAbsoluteUrl(string x)
    {
        return string.IsNullOrWhiteSpace(x)
            || (Uri.TryCreate(x, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
    }
}
