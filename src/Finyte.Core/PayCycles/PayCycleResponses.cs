namespace Finyte.Core.PayCycles;

public sealed record PayCycleProfileResponse(Guid Id, string Name, string Frequency, DateOnly AnchorDate, string Currency,
    decimal? ExpectedIncome, Guid[] AccountIds, Guid[] SavingsAccountIds, int Version, DateTimeOffset UpdatedAt);

public sealed record PayCycleAccountResponse(Guid Id, string Name, string Currency, bool IncludeInAnalytics);

public sealed record PayCycleBreakdownResponse(PayCycleProfileResponse Profile, DateOnly From, DateOnly To,
    DateOnly? ObservedThrough, DateOnly? PreviousDate, DateOnly? NextDate, string PeriodStatus, string DateBasis,
    IReadOnlyList<PayCycleAccountResponse> Accounts, IReadOnlyList<PayCycleAccountResponse> SavingsAccounts,
    IReadOnlyList<Guid> MissingAccountIds, PayCycleTotals Totals, IReadOnlyList<PayCycleCategory> SpendingCategories,
    int UndatedTransactionCount, int UnpostedTransactionCount, int OtherCurrencyTransactionCount,
    PayCycleTransactionPage Transactions);

public sealed record PayCycleTotals(decimal ExternalCredits, decimal Spending, decimal SavingsTransfersOut, decimal SavingsTransfersIn, decimal NetSavingsTransfers,
    decimal OtherTransfersOut, decimal TransfersIn, decimal WithinScopeTransfers, decimal NetMovement,
    decimal? ExpectedIncomeDifference, int TransactionCount);

public sealed record PayCycleCategory(string Name, decimal Amount, int TransactionCount);

public sealed record PayCycleTransactionPage(int Page, int PageSize, int TotalCount, string? Kind, IReadOnlyList<PayCycleTransactionResponse> Items);

public sealed record PayCycleTransactionResponse(Guid Id, Guid AccountId, string AccountName, string? Description,
    string? MerchantName, decimal Amount, DateTimeOffset PostedAt, DateOnly PostedDate, string Kind, string Category);
