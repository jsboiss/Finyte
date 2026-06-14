namespace Finyte.Core.Accounts;

public sealed class TransactionTagAssignment
{
    public Guid TransactionId { get; set; }
    public Transaction? Transaction { get; set; }
    public Guid TagId { get; set; }
    public TransactionTag? Tag { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
