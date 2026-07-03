namespace Finyte.Api.Billing;

public sealed class StripeOptions
{
    public static string SectionName => "Stripe";

    public string SecretKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";

    public Dictionary<string, string> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string SuccessUrl { get; set; } = "";

    public string CancelUrl { get; set; } = "";

    public string PortalReturnUrl { get; set; } = "";

    public bool HasCheckoutConfiguration =>
        !string.IsNullOrWhiteSpace(SecretKey)
        && Prices.Count > 0
        && !string.IsNullOrWhiteSpace(SuccessUrl)
        && !string.IsNullOrWhiteSpace(CancelUrl);

    public bool HasPortalConfiguration =>
        !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(PortalReturnUrl);

    public bool HasWebhookConfiguration =>
        !string.IsNullOrWhiteSpace(WebhookSecret);

    public bool TryGetPriceId(string planKey, out string priceId)
    {
        return Prices.TryGetValue(planKey, out priceId!)
            && !string.IsNullOrWhiteSpace(priceId);
    }
}
