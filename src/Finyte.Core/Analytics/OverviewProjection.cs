namespace Finyte.Core.Analytics;

public sealed class OverviewProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? AccountId { get; set; }
    public string MonthKey { get; set; } = "";
    public string Currency { get; set; } = "AUD";
    public string PayloadJson { get; set; } = "";
    public long SourceVersion { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public long Generation { get; set; }
    public string Status { get; set; } = ProjectionStatus.Pending;
    public string? LastError { get; set; }
    public string? TemporalWorkflowId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public DateTimeOffset? SourceWatermark { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
