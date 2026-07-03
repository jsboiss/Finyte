using Finyte.Core.Tenancy;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Finyte.Api.Tenancy;

public sealed class TenantResolver(FinyteDbContext dbContext)
{
    public async Task<CurrentTenant> Resolve(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var member = await dbContext.TenantMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new CurrentTenant(x.UserId, x.TenantId, x.Role))
            .SingleOrDefaultAsync(cancellationToken);

        if (member is not null)
        {
            return member;
        }

        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant
        {
            Name = "My household",
            CreatedAt = now
        };

        var tenantMember = new TenantMember
        {
            TenantId = tenant.Id,
            UserId = userId,
            Role = TenantRole.Owner,
            CreatedAt = now
        };

        dbContext.Tenants.Add(tenant);
        dbContext.TenantMembers.Add(tenantMember);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CurrentTenant(userId, tenant.Id, tenantMember.Role);
    }

    private static string GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user does not have a subject claim.");
    }
}
