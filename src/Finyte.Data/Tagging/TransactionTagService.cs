using Finyte.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finyte.Data.Tagging;

public sealed class TransactionTagService(FinyteDbContext dbContext)
{
    public async Task<IDbContextTransaction?> BeginMutation(Guid tenantId, CancellationToken cancellationToken)
    {
        var databaseTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await Lock(tenantId, cancellationToken);
            return databaseTransaction;
        }
        catch
        {
            if (databaseTransaction is not null)
            {
                await databaseTransaction.DisposeAsync();
            }
            throw;
        }
    }

    public async Task Lock(Guid tenantId, CancellationToken cancellationToken)
    {
        // All tag writers share this transaction-scoped lock: sync cannot undo a concurrent manual edit.
        if (dbContext.Database.IsNpgsql())
        {
            var lockKey = $"transaction-tagging:{tenantId:N}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MerchantTagRule>> GetRules(Guid tenantId, CancellationToken cancellationToken)
    {
        await dbContext.MerchantTagRules.Where(x => x.TenantId == tenantId && x.Tag != null && x.Tag.TenantId == tenantId)
            .LoadAsync(cancellationToken);
        // Include an added/edited rule and exclude a deleted rule before the atomic save.
        var rules = dbContext.MerchantTagRules.Local.Where(x => x.TenantId == tenantId).ToList();
        // Stored matching words are user-owned semantics. Only an explicit rule edit changes them.
        return rules.OrderByDescending(x => x.MerchantKey.Length).ThenBy(x => x.Id).ToList();
    }

    public bool Reconcile(Transaction transaction, IReadOnlyList<MerchantTagRule> rules)
    {
        var excludedIds = transaction.TagExclusions.Select(x => x.TagId).ToHashSet();
        var matchingRules = rules
            .Where(x => x.TenantId == transaction.TenantId && !excludedIds.Contains(x.TagId)
                && MerchantTagMatcher.Matches(transaction.MerchantName, transaction.Description, x.MerchantKey))
            .GroupBy(x => x.TagId).ToDictionary(x => x.Key, x => x.First());
        var changed = false;

        foreach (var assignment in transaction.TagAssignments.ToList())
        {
            if (assignment.Source != TransactionTagSource.MerchantRule)
            {
                // Legacy assignments have unknown provenance. Never guess that they can be deleted.
                matchingRules.Remove(assignment.TagId);
                continue;
            }

            if (!matchingRules.Remove(assignment.TagId, out var rule))
            {
                transaction.TagAssignments.Remove(assignment);
                dbContext.TransactionTagAssignments.Remove(assignment);
                changed = true;
            }
            else if (assignment.MerchantRuleId != rule.Id)
            {
                assignment.MerchantRuleId = rule.Id;
                assignment.MerchantRule = rule;
                changed = true;
            }
        }

        foreach (var rule in matchingRules.Values)
        {
            transaction.TagAssignments.Add(new TransactionTagAssignment
            {
                TransactionId = transaction.Id, TagId = rule.TagId, Source = TransactionTagSource.MerchantRule,
                MerchantRuleId = rule.Id, MerchantRule = rule, CreatedAt = DateTimeOffset.UtcNow
            });
            changed = true;
        }

        return changed;
    }

    public async Task<bool> ReplaceMerchantRules(Guid tenantId, string merchantName, IReadOnlyCollection<TransactionTag> tags, CancellationToken cancellationToken)
    {
        var merchantKey = MerchantTagMatcher.Normalize(merchantName);
        var rules = await GetRules(tenantId, cancellationToken);
        var tagIds = tags.Select(x => x.Id).ToHashSet();
        var changed = false;
        foreach (var rule in rules.Where(x => x.MerchantKey == merchantKey && !tagIds.Contains(x.TagId)))
        {
            dbContext.MerchantTagRules.Remove(rule);
            changed = true;
        }
        foreach (var tag in tags.Where(x => !rules.Any(y => y.MerchantKey == merchantKey && y.TagId == x.Id)))
        {
            dbContext.MerchantTagRules.Add(new MerchantTagRule
            {
                TenantId = tenantId, MerchantName = merchantName, MerchantKey = merchantKey, TagId = tag.Id, Tag = tag, CreatedAt = DateTimeOffset.UtcNow
            });
            changed = true;
        }
        return changed;
    }

    public async Task<bool> ReconcileTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(tenantId, cancellationToken);
        var changed = false;
        // Bounded reads avoid loading the entire ledger and both collections in one query.
        var skip = 0;
        while (true)
        {
            var transactions = await dbContext.Transactions.Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.Id).Skip(skip).Take(500)
                .Include(x => x.TagAssignments).Include(x => x.TagExclusions)
                .ToListAsync(cancellationToken);
            foreach (var transaction in transactions)
            {
                changed |= Reconcile(transaction, rules);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var transaction in transactions)
            {
                foreach (var assignment in transaction.TagAssignments)
                {
                    dbContext.Entry(assignment).State = EntityState.Detached;
                }
                foreach (var exclusion in transaction.TagExclusions)
                {
                    dbContext.Entry(exclusion).State = EntityState.Detached;
                }
                dbContext.Entry(transaction).State = EntityState.Detached;
            }
            if (transactions.Count < 500)
            {
                break;
            }
            skip += transactions.Count;
        }
        return changed;
    }

    public void SetTags(Transaction transaction, IReadOnlySet<Guid> selectedIds, IReadOnlySet<Guid> manualIds)
    {
        foreach (var assignment in transaction.TagAssignments.ToList())
        {
            if (!selectedIds.Contains(assignment.TagId))
            {
                if (!transaction.TagExclusions.Any(x => x.TagId == assignment.TagId))
                {
                    transaction.TagExclusions.Add(new TransactionTagExclusion
                    {
                        TransactionId = transaction.Id, TagId = assignment.TagId, CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                transaction.TagAssignments.Remove(assignment);
                dbContext.TransactionTagAssignments.Remove(assignment);
            }
            else if (manualIds.Contains(assignment.TagId))
            {
                assignment.Source = TransactionTagSource.Manual;
                assignment.MerchantRuleId = null;
                assignment.MerchantRule = null;
            }
        }

        var exclusions = transaction.TagExclusions.Where(x => selectedIds.Contains(x.TagId)).ToList();
        foreach (var exclusion in exclusions)
        {
            transaction.TagExclusions.Remove(exclusion);
            dbContext.TransactionTagExclusions.Remove(exclusion);
        }
        var existingIds = transaction.TagAssignments.Select(x => x.TagId).ToHashSet();
        foreach (var selectedId in selectedIds.Where(x => !existingIds.Contains(x)))
        {
            transaction.TagAssignments.Add(new TransactionTagAssignment
            {
                TransactionId = transaction.Id, TagId = selectedId, Source = TransactionTagSource.Manual,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    public void PrepareForMerchantRule(Transaction transaction, IReadOnlySet<Guid> selectedIds)
    {
        foreach (var assignment in transaction.TagAssignments.Where(x => !selectedIds.Contains(x.TagId)).ToList())
        {
            if (transaction.TagExclusions.All(x => x.TagId != assignment.TagId))
            {
                transaction.TagExclusions.Add(new TransactionTagExclusion { TransactionId = transaction.Id, TagId = assignment.TagId, CreatedAt = DateTimeOffset.UtcNow });
            }
            transaction.TagAssignments.Remove(assignment);
            dbContext.TransactionTagAssignments.Remove(assignment);
        }
        foreach (var exclusion in transaction.TagExclusions.Where(x => selectedIds.Contains(x.TagId)).ToList())
        {
            transaction.TagExclusions.Remove(exclusion);
            dbContext.TransactionTagExclusions.Remove(exclusion);
        }
    }

    public void ClearExclusions(Transaction transaction)
    {
        dbContext.TransactionTagExclusions.RemoveRange(transaction.TagExclusions);
        transaction.TagExclusions.Clear();
    }
}
