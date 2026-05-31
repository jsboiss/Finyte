namespace Finyte.Core.Accounts;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string? FiskilAccountId { get; set; }
    public string? AccountNumber { get; set; }
    public string? Bsb { get; set; }
    public required string Name { get; set; }
    public string? ProductName { get; set; }
    public string? ProductCategory { get; set; }
    public string? InstitutionId { get; set; }
    public string? ConsentId { get; set; }
    public bool? IsOwned { get; set; }
    public string? OpenStatus { get; set; }
    public DateOnly? CreationDate { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public string Currency { get; set; } = "AUD";
    public DateTimeOffset? BalanceAsOf { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
}
