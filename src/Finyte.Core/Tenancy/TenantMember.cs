namespace Finyte.Core.Tenancy;

public sealed class TenantMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public required string UserId { get; set; }
    public TenantRole Role { get; set; } = TenantRole.Owner;
    public DateTimeOffset CreatedAt { get; set; }
}
