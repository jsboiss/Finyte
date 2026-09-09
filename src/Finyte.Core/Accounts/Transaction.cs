namespace Finyte.Core.Accounts;

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public required string FiskilTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string? Description { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? PrimaryCategory { get; set; }
    public string? SecondaryCategory { get; set; }
    public string? MerchantName { get; set; }
    public string? Reference { get; set; }
    public string? RawJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TransactionTagAssignment> TagAssignments { get; set; } = [];
    public ICollection<TransactionTagExclusion> TagExclusions { get; set; } = [];
}
