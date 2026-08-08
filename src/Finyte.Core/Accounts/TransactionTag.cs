namespace Finyte.Core.Accounts;

public sealed class TransactionTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TransactionTagAssignment> TransactionAssignments { get; set; } = [];
    public ICollection<MerchantTagRule> MerchantRules { get; set; } = [];
}
