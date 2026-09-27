using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed record ResolvedAccountScope(Guid? AccountId, IReadOnlyList<Guid>? AccountIds, string? Label)
{
    public bool IsMultiple => AccountIds is not null;
}

public static class AccountScopeResolver
{
    public static async Task<ResolvedAccountScope> Resolve(FinyteDbContext dbContext, Guid tenantId, Guid? accountId, IReadOnlyCollection<Guid>? accountIds,
        Guid? groupId, CancellationToken cancellationToken)
    {
        string? label = null;
        List<Guid> ids;
        if (groupId is { } id)
        {
            var group = await dbContext.AccountGroups.AsNoTracking().Include(x => x.Members)
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("Account group not found.");
            ids = group.Members.Select(x => x.AccountId).Distinct().ToList();
            label = group.Name;
        }
        else if (accountIds is { Count: > 0 })
        {
            if (accountIds.Count > 100 || accountIds.Contains(Guid.Empty))
            {
                throw new ArgumentException("Choose at most 100 accounts.");
            }
            ids = accountIds.Distinct().ToList();
        }
        else if (accountId is { } single)
        {
            ids = [single];
        }
        else
        {
            return new ResolvedAccountScope(null, null, null);
        }

        var owned = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id)).CountAsync(cancellationToken);
        if (owned != ids.Count || ids.Count == 0)
        {
            throw new KeyNotFoundException("Account not found.");
        }
        return ids.Count == 1 && groupId is null
            ? new ResolvedAccountScope(ids[0], null, null)
            : new ResolvedAccountScope(null, ids, label ?? $"{ids.Count} accounts");
    }
}
