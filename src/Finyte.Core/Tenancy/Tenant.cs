namespace Finyte.Core.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TenantMember> Members { get; set; } = [];
}
