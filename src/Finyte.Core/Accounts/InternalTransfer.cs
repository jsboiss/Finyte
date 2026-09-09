namespace Finyte.Core.Accounts;

public sealed class InternalTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DebitTransactionId { get; set; }
    public Transaction DebitTransaction { get; set; } = null!;
    public Guid CreditTransactionId { get; set; }
    public Transaction CreditTransaction { get; set; } = null!;
    public required string Status { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid DebitAccountId { get; set; }
    public Guid CreditAccountId { get; set; }
    public DateTimeOffset DebitPostedAt { get; set; }
    public DateTimeOffset CreditPostedAt { get; set; }
    public required string ReviewedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
