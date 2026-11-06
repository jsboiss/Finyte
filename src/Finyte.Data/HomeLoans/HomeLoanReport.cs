using Finyte.Core.HomeLoans;

namespace Finyte.Data.HomeLoans;

public sealed record HomeLoanReport(
    string Currency,
    int Months,
    string FromMonth,
    string ToMonth,
    IReadOnlyList<string> AvailableMonths,
    HomeLoanSetup Setup,
    IReadOnlyList<HomeLoanMonth> MonthlyTotals,
    HomeLoanMonth Average,
    HomeLoanMonth Total,
    IReadOnlyList<HomeLoanCategoryTotal> Categories,
    IReadOnlyList<HomeLoanTagChoice> Tags,
    IReadOnlyList<HomeLoanPayer> Payers,
    IReadOnlyList<HomeLoanMerchant> NeedsLabel,
    IReadOnlyList<HomeLoanFlag> Flags,
    IReadOnlyList<HomeLoanGap> Gaps,
    IReadOnlyList<HomeLoanCategory> LenderCategories);

public sealed record HomeLoanSetup(string FirstApplicant, string? SecondApplicant, IReadOnlyList<HomeLoanAccountRole> Accounts, int Version, bool Saved);

public sealed record HomeLoanAccountRole(Guid AccountId, string Name, string Type, string Role);

public sealed record HomeLoanMonth(string Month, decimal FirstApplicantIncome, decimal SecondApplicantIncome, decimal OtherIncome,
    decimal UnlabelledCredits, decimal Refunds, decimal Gifts, decimal LivingExpenses, decimal Commitments, decimal Saved, decimal Surplus);

public sealed record HomeLoanCategoryTotal(string Key, string Label, string Group, decimal Total, decimal MonthlyAverage, IReadOnlyList<decimal> ByMonth);

public sealed record HomeLoanTagChoice(Guid TagId, string Name, string Color, string Category, bool IsDefault, decimal Total);

public sealed record HomeLoanPayer(string Key, string Name, string? PaymentType, string? Kind, int? Applicant, int Count, decimal Total, int MonthsSeen,
    DateOnly LatestDate, IReadOnlyList<HomeLoanPayment> Payments, string? SuggestedKind);

public sealed record HomeLoanMerchant(string Key, string Name, bool Confirmed, IReadOnlyList<string> PaymentTypes, string? SuggestedTag, int Count, decimal Total,
    IReadOnlyList<HomeLoanMerchantVariant> Variants, IReadOnlyList<HomeLoanPayment> Payments);

public sealed record HomeLoanMerchantVariant(string RuleMerchantName, string OwnName, int Count, decimal Total);

public sealed record HomeLoanPayment(Guid TransactionId, DateOnly Date, string Account, string Description, decimal Amount);

public sealed record HomeLoanFlag(string Kind, string Label, string Advice, int Count, decimal Total, IReadOnlyList<HomeLoanFlagItem> Items);

public sealed record HomeLoanFlagItem(DateOnly Date, string Account, string Description, decimal Amount);

public sealed record HomeLoanGap(Guid AccountId, string AccountName, IReadOnlyList<string> MissingMonths);

public sealed record HomeLoanRow(DateOnly Date, string Account, string Role, string Description, decimal Amount, string Type,
    string? Applicant, string? Category, string Tags, string? Flag);
