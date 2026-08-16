namespace Finyte.Core.Tenancy;

public sealed class ClerkWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string MessageId { get; set; }
    public required string EventType { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
