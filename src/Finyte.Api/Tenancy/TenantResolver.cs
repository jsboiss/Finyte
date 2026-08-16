using System.Security.Claims;
using System.Text.Json;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Tenancy;

public sealed class TenantResolver(FinyteDbContext dbContext, IWebHostEnvironment environment)
{
    public async Task<CurrentTenant?> TryResolve(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = TryGetIdentity(user);
        if (identity is null)
        {
            return null;
        }

        return await dbContext.TenantMembers
            .AsNoTracking()
            .Where(x => x.UserId == identity.UserId && x.RemovedAt == null && x.Tenant!.ClerkOrganizationId == identity.OrganizationId)
            .Select(x => new CurrentTenant(x.UserId, x.TenantId, x.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CurrentTenant> Resolve(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var currentTenant = await TryResolve(user, cancellationToken);
        if (currentTenant is not null)
        {
            return currentTenant;
        }

        if (environment.IsDevelopment())
        {
            return await Provision(user, "Dev household", cancellationToken);
        }

        throw new TenantNotProvisionedException();
    }

    public async Task<CurrentTenant> Provision(ClaimsPrincipal user, string familyName, CancellationToken cancellationToken)
    {
        var identity = TryGetIdentity(user) ?? throw new ActiveOrganizationRequiredException();
        var currentTenant = await TryResolve(user, cancellationToken);
        if (currentTenant is not null)
        {
            return currentTenant;
        }

        var tenant = await dbContext.Tenants
            .SingleOrDefaultAsync(x => x.ClerkOrganizationId == identity.OrganizationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (tenant is null)
        {
            tenant = new Tenant
            {
                ClerkOrganizationId = identity.OrganizationId,
                Name = familyName,
                CreatedAt = now
            };
            dbContext.Tenants.Add(tenant);
        }

        var tenantMember = new TenantMember
        {
            TenantId = tenant.Id,
            UserId = identity.UserId,
            Role = GetTenantRole(identity.OrganizationRole),
            CreatedAt = now
        };
        dbContext.TenantMembers.Add(tenantMember);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CurrentTenant(identity.UserId, tenant.Id, tenantMember.Role);
    }

    private static ClerkIdentity? TryGetIdentity(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user does not have a subject claim.");
        var organizationClaim = GetOrganizationClaim(user);
        var organizationId = user.FindFirstValue("org_id") ?? organizationClaim?.OrganizationId;
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return null;
        }
        var organizationRole = user.FindFirstValue("org_role") ?? organizationClaim?.OrganizationRole;

        return new ClerkIdentity(userId, organizationId, organizationRole);
    }

    private static TenantRole GetTenantRole(string? organizationRole)
    {
        return organizationRole is "org:admin" or "admin" ? TenantRole.Owner : TenantRole.Member;
    }

    private static ClerkOrganizationClaim? GetOrganizationClaim(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("o");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        using var document = JsonDocument.Parse(value);
        var root = document.RootElement;
        var organizationId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var organizationRole = root.TryGetProperty("rol", out var role) ? role.GetString() : null;

        return string.IsNullOrWhiteSpace(organizationId)
            ? null
            : new ClerkOrganizationClaim(organizationId, organizationRole);
    }

    private sealed record ClerkIdentity(string UserId, string OrganizationId, string? OrganizationRole);

    private sealed record ClerkOrganizationClaim(string OrganizationId, string? OrganizationRole);
}

public sealed class ActiveOrganizationRequiredException : InvalidOperationException
{
    public ActiveOrganizationRequiredException() : base("Select or create a family before continuing.")
    {
    }
}

public sealed class TenantNotProvisionedException : InvalidOperationException
{
    public TenantNotProvisionedException() : base("The active family has not been provisioned.")
    {
    }
}
