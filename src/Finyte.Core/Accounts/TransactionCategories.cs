namespace Finyte.Core.Accounts;

public static class TransactionCategories
{
    public const string Uncategorised = "Uncategorised";

    public static string Effective(string? categoryOverride, string? secondaryCategory, string? primaryCategory)
    {
        if (!string.IsNullOrWhiteSpace(categoryOverride))
        {
            return categoryOverride.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondaryCategory))
        {
            return secondaryCategory.Trim();
        }

        return string.IsNullOrWhiteSpace(primaryCategory) ? Uncategorised : primaryCategory.Trim();
    }

    public static string Effective(Transaction transaction)
    {
        return Effective(transaction.CategoryOverride, transaction.SecondaryCategory, transaction.PrimaryCategory);
    }
}
