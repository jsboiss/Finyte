namespace Finyte.Core.Accounts;

public sealed class MerchantVendor
{
    public Guid TenantId { get; set; }
    public required string MatchKey { get; set; }
    public required string Vendor { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
