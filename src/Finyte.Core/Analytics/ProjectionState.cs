namespace Finyte.Core.Analytics;

public sealed class ProjectionState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ProjectionKey { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public string ScopeJson { get; set; } = "";
    public string Status { get; set; } = ProjectionStatus.Pending;
    public bool IsStale { get; set; }
    public DateTimeOffset? StaleAt { get; set; }
    public string? StaleReason { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
