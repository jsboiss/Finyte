namespace Finyte.Core.PayCycles;

public sealed class PayCycleProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Frequency { get; set; }
    public DateOnly AnchorDate { get; set; }
    public required string Currency { get; set; }
    public decimal? ExpectedIncome { get; set; }
    public Guid[] AccountIds { get; set; } = [];
    public Guid[] SavingsAccountIds { get; set; } = [];
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
