namespace Finyte.Core.Accounts;

public sealed class TransactionTagAssignment
{
    public Guid TransactionId { get; set; }
    public Transaction? Transaction { get; set; }
    public Guid TagId { get; set; }
    public TransactionTag? Tag { get; set; }
    public string Source { get; set; } = TransactionTagSource.Legacy;
    public Guid? MerchantRuleId { get; set; }
    public MerchantTagRule? MerchantRule { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class TransactionTagSource
{
    public const string Legacy = "legacy";
    public const string Manual = "manual";
    public const string MerchantRule = "merchant-rule";
    public const string System = "system";
    public const string BankCategory = "bank-category";
}
