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
    OverviewFreshnessResponse Freshness);

public sealed record OverviewScopeResponse(Guid? AccountId, string Label);

public sealed record OverviewCashFlowRaceResponse(long IncomeMinorUnits, long ExpenseMinorUnits, long NetMinorUnits);

public sealed record OverviewDailyCashFlowResponse(string Date, int Day, long IncomeMinorUnits, long ExpenseMinorUnits);

public sealed record OverviewMonthlySpendByTagResponse(Guid? TagId, string Name, string Color, long AmountMinorUnits, decimal Percentage);

public sealed record OverviewFreshnessResponse(DateTimeOffset CalculatedAt, DateTimeOffset? SourceWatermark, bool IsRefreshing);

public sealed record CashFlowRangeResponse(
    string From,
    string To,
    string Currency,
    IReadOnlyList<OverviewDailyCashFlowResponse> DailyCashFlow);
