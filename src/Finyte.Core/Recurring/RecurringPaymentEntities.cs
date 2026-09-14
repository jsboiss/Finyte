namespace Finyte.Core.Recurring;

public sealed class RecurringPaymentSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public Guid AccountId { get; set; }
    public required string Currency { get; set; }
    public required string Cadence { get; set; }
    public DateOnly AnchorDate { get; set; }
    public decimal ExpectedAmount { get; set; }
    public required string AmountMode { get; set; }
    public string State { get; set; } = "active";
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<RecurringPaymentAlias> Aliases { get; set; } = [];
}

public sealed class RecurringPaymentAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriesId { get; set; }
    public required string Field { get; set; }
    public required string Value { get; set; }
    public required string NormalizedValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RecurringPaymentDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SeriesId { get; set; }
    public Guid TransactionId { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public required string Status { get; set; }
    public required string Fingerprint { get; set; }
    public required string SnapshotJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Append-only review evidence. Subsequent decisions never rewrite earlier snapshots.
public sealed class RecurringPaymentReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SeriesId { get; set; }
    public Guid TransactionId { get; set; }
    public DateOnly OccurrenceDate { get; set; }
    public required string Action { get; set; }
    public required string SnapshotJson { get; set; }
    public required string ReviewedByUserId { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
}

public sealed class RecurringDiscoveryDecision
{
    public Guid TenantId { get; set; }
    public required string CandidateKey { get; set; }
    public DateTimeOffset DismissedAt { get; set; }
}
