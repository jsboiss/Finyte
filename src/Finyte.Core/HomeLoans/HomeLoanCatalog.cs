namespace Finyte.Core.HomeLoans;

public sealed record HomeLoanCategory(string Key, string Label, string Group);

public static class HomeLoanCatalog
{
    public const string Living = "living";
    public const string Commitment = "commitment";
    public const string Savings = "savings";
    public const string Untagged = "untagged";
    public const string Unmapped = "unmapped";
    public const string Review = "review";
    public const decimal LargeDepositThreshold = 1000m;

    public static IReadOnlyList<HomeLoanCategory> Categories { get; } =
    [
        new("food-groceries", "Food & groceries", Living),
        new("residence", "Rates, utilities & home maintenance", Living),
        new("phone-internet-media", "Phone, internet & media", Living),
        new("transport", "Transport", Living),
        new("clothing-personal-care", "Clothing & personal care", Living),
        new("recreation", "Recreation & holidays", Living),
        new("medical", "Medical & health", Living),
        new("general-insurance", "Insurance (home, contents, car)", Living),
        new("life-insurance", "Life, accident & illness insurance", Living),
        new("childcare-education", "Childcare, public education & dependants", Living),
        new("private-school", "Private school fees", Living),
        new("strata", "Strata / body corporate", Living),
        new("other-living", "Other living expenses", Living),
        new("rent", "Rent", Commitment),
        new("loan-repayments", "Loan repayments", Commitment),
        new("credit-card", "Credit card repayments", Commitment),
        new("bnpl", "Buy now pay later", Commitment),
        new("child-support", "Child support", Commitment),
        new("savings-elsewhere", "Moved to savings at another bank", Savings)
    ];

    public static IReadOnlyList<string> IncomeKinds { get; } = ["salary", "other-income", "refund", "gift", "not-income"];

    public static HomeLoanCategory? Category(string? key) => Categories.FirstOrDefault(x => x.Key == key);

    public static string Label(string key) => key switch
    {
        Untagged => "Not tagged yet",
        Unmapped => "Tags without a lender category",
        Review => "Needs review: tags point to different categories",
        _ => Category(key)?.Label ?? key
    };

    public static string Group(string key) => Category(key)?.Group ?? Living;
}
