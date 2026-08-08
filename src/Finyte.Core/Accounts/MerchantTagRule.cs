namespace Finyte.Core.Accounts;

public sealed class MerchantTagRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string MerchantName { get; set; }
    public required string MerchantKey { get; set; }
    public Guid TagId { get; set; }
    public TransactionTag? Tag { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
