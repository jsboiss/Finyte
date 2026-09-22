namespace Finyte.Core.Analytics;

public sealed record OverviewResponse(
    OverviewScopeResponse Scope,
    string MonthKey,
    string Currency,
    long AccountBalanceMinorUnits,
    long CurrentMonthSpendMinorUnits,
    long AverageDailySpendMinorUnits,
    OverviewCashFlowRaceResponse CashFlowRace,
    IReadOnlyList<OverviewDailyCashFlowResponse> DailyCashFlow,
    IReadOnlyList<OverviewMonthlySpendByTagResponse> MonthlySpendByTag,
    OverviewFreshnessResponse Freshness,
    // Nullable so projections persisted before this field existed still deserialize.
    OverviewBalanceCoverageResponse? BalanceCoverage = null);

// A balance is only real when its source reported one. Callers need the scope so a partial total is never
// presented as complete, and the names so the user can see which accounts are holding it back.
public sealed record OverviewBalanceCoverageResponse(
    int CoveredAccounts,
    int TotalAccounts,
    IReadOnlyList<string> MissingAccounts);

public sealed record OverviewScopeResponse(Guid? AccountId, string Label);

public sealed record OverviewCashFlowRaceResponse(long IncomeMinorUnits, long ExpenseMinorUnits, long NetMinorUnits);

public sealed record OverviewDailyCashFlowResponse(string Date, int Day, long IncomeMinorUnits, long ExpenseMinorUnits);

public sealed record OverviewMonthlySpendByTagResponse(Guid? TagId, string Name, string Color, long AmountMinorUnits, decimal Percentage);

public sealed record OverviewFreshnessResponse(
    DateTimeOffset CalculatedAt,
    DateTimeOffset? SourceWatermark,
    bool IsRefreshing,
    bool IsStale = false,
    bool HasFailed = false,
    string? LastError = null);

public sealed record CashFlowRangeResponse(
    string From,
    string To,
    string Currency,
    IReadOnlyList<OverviewDailyCashFlowResponse> DailyCashFlow);
