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
    HomeLoanUntagged Untagged,
    IReadOnlyList<HomeLoanReviewPayment> Review,
    IReadOnlyList<HomeLoanFlag> Flags,
    IReadOnlyList<HomeLoanGap> Gaps,
    IReadOnlyList<HomeLoanCategory> LenderCategories);

public sealed record HomeLoanSetup(string FirstApplicant, string? SecondApplicant, IReadOnlyList<HomeLoanAccountRole> Accounts, int Version, bool Saved);

public sealed record HomeLoanAccountRole(Guid AccountId, string Name, string Type, string Role);

public sealed record HomeLoanMonth(string Month, decimal FirstApplicantIncome, decimal SecondApplicantIncome, decimal OtherIncome,
    decimal UnlabelledCredits, decimal Refunds, decimal Gifts, decimal LivingExpenses, decimal Commitments, decimal Saved, decimal Surplus);

public sealed record HomeLoanCategoryTotal(string Key, string Label, string Group, decimal Total, decimal MonthlyAverage, IReadOnlyList<decimal> ByMonth);

public sealed record HomeLoanTagChoice(Guid TagId, string Name, string Color, string? Category, decimal Total, IReadOnlyList<HomeLoanPayment> Payments);

public sealed record HomeLoanPayer(string Key, string Name, string? MatchedBy, string? Kind, int? Applicant, int Count, decimal Total, int MonthsSeen,
    DateOnly LatestDate, IReadOnlyList<HomeLoanPayment> Payments);

public sealed record HomeLoanUntagged(int Count, decimal Total);

public sealed record HomeLoanPayment(Guid TransactionId, DateOnly Date, string Account, string Description, decimal Amount, string? OwnCategory);

public sealed record HomeLoanReviewPayment(Guid TransactionId, DateOnly Date, string Account, string Description, decimal Amount,
    IReadOnlyList<string> Tags, string? OwnCategory);

public sealed record HomeLoanFlag(string Kind, string Label, string Advice, int Count, decimal Total, IReadOnlyList<HomeLoanFlagItem> Items);

public sealed record HomeLoanFlagItem(DateOnly Date, string Account, string Description, decimal Amount);

public sealed record HomeLoanGap(Guid AccountId, string AccountName, IReadOnlyList<string> MissingMonths);

public sealed record HomeLoanRow(DateOnly Date, string Account, string Role, string Description, decimal Amount, string Type,
    string? Applicant, string? Category, string Tags, string? Flag);
