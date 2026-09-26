namespace Finyte.Core.Accounts;

public static class TransactionCategories
{
    public const string Uncategorised = "Uncategorised";

    public static string Effective(string? categoryOverride, string? secondaryCategory, string? primaryCategory, string? categoryFromRule)
    {
        if (!string.IsNullOrWhiteSpace(categoryOverride))
        {
            return categoryOverride.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondaryCategory))
        {
            return secondaryCategory.Trim();
        }

        if (!string.IsNullOrWhiteSpace(primaryCategory))
        {
            return primaryCategory.Trim();
        }

        return string.IsNullOrWhiteSpace(categoryFromRule) ? Uncategorised : categoryFromRule.Trim();
    }

    public static string Effective(Transaction transaction)
    {
        return Effective(transaction.CategoryOverride, transaction.SecondaryCategory, transaction.PrimaryCategory, transaction.CategoryFromRule);
    }
}
