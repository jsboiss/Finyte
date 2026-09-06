namespace Finyte.Core.Accounts;

public sealed class TransactionFileImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public required string FileName { get; set; }
    public string Status { get; set; } = "running";
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalCount { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TransactionFileIdentity
{
    public Guid TenantId { get; set; }
    public required string ExternalId { get; set; }
    public Guid TransactionId { get; set; }
}
