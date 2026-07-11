namespace Finyte.Api.Tenancy;

public sealed class ClerkWebhookOptions
{
    public const string SectionName = "Clerk";

    public string WebhookSigningSecret { get; set; } = "";
}
