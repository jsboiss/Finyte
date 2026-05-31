namespace Finyte.Core.Analytics;

public sealed class OverviewProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? AccountId { get; set; }
    public string MonthKey { get; set; } = "";
    public string Currency { get; set; } = "AUD";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset? SourceWatermark { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
