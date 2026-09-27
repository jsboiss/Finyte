namespace Finyte.Core.Accounts;

public sealed class AccountGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<AccountGroupMember> Members { get; set; } = [];
}

public sealed class AccountGroupMember
{
    public Guid GroupId { get; set; }
    public AccountGroup? Group { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
}
