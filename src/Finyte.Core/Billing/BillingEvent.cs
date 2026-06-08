namespace Finyte.Core.Billing;

public sealed class BillingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string StripeEventId { get; set; }
    public required string Type { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}
