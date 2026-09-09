namespace Finyte.Core.Accounts;

// A user's removal must survive the next import, sync, or matching rule change.
public sealed class TransactionTagExclusion
{
    public Guid TransactionId { get; set; }
    public Transaction? Transaction { get; set; }
    public Guid TagId { get; set; }
    public TransactionTag? Tag { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
