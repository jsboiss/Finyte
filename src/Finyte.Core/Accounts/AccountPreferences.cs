namespace Finyte.Core.Accounts;

public static class AccountPreferences
{
    public static IReadOnlyList<string> Types { get; } = ["everyday", "savings", "credit-card", "home-loan", "offset", "loan", "investment", "term-deposit", "other"];

    // Categories verified against .codex/skills/fiskil-api/references/data-api/api-reference/accounts.mdx.
    // A shared transaction/savings category cannot distinguish savings or offset accounts.
    public static string InferredType(string? productCategory) => productCategory?.Trim().ToUpperInvariant() switch
    {
        "TRANS_AND_SAVINGS_ACCOUNTS" => "everyday",
        "CRED_AND_CHRG_CARDS" => "credit-card",
        "RESIDENTIAL_MORTGAGES" => "home-loan",
        "PERS_LOANS" => "loan",
        "TERM_DEPOSITS" => "term-deposit",
        _ => "other"
    };

    public static string EffectiveType(Account account) => account.AccountTypeOverride ?? InferredType(account.ProductCategory);

    public static bool DefaultIncludeInAnalytics(string accountType) => accountType is not "home-loan" and not "loan" and not "investment" and not "term-deposit";

    public static bool IncludeInAnalytics(Account account) => account.IncludeInAnalyticsOverride ?? DefaultIncludeInAnalytics(EffectiveType(account));

    public static string DisplayName(Account account) => account.CustomName ?? account.Name;

    public static bool IsProviderManaged(Account account) => account.ProviderConnectionId.HasValue || !string.IsNullOrWhiteSpace(account.FiskilAccountId);
}
