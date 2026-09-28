using Finyte.Core.Accounts;

namespace Finyte.Data.Tagging;

public sealed class CategoryTagSet(FinyteDbContext dbContext, Guid tenantId, List<TransactionTag> tags)
{
    private const string DefaultColor = "#64748b";

    public TransactionTag? Resolve(Transaction transaction, IReadOnlySet<Guid> excludedIds)
    {
        if (transaction.TenantId != tenantId || transaction.FiskilTransactionId.StartsWith("file:", StringComparison.Ordinal)
            || MerchantKeywordCatalog.CategoryTag(transaction.PrimaryCategory, transaction.SecondaryCategory) is not { } name)
        {
            return null;
        }
        var tag = tags.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            var starter = MerchantKeywordCatalog.Starter(name);
            tag = new TransactionTag { TenantId = tenantId, Name = starter?.Name ?? name, Color = starter?.Color ?? DefaultColor, CreatedAt = DateTimeOffset.UtcNow };
            dbContext.TransactionTags.Add(tag);
            tags.Add(tag);
        }
        return excludedIds.Contains(tag.Id) ? null : tag;
    }
}
