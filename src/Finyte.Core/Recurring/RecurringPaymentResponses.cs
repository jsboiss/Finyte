namespace Finyte.Core.Recurring;

public sealed record RecurringAliasInput(string Field, string Value);
public sealed record RecurringHistoryInput(Guid TransactionId, DateOnly OccurrenceDate, string Fingerprint);
public sealed record CreateRecurringRequest(string Name, Guid AccountId, string Currency, string Cadence, DateOnly AnchorDate,
    decimal ExpectedAmount, string AmountMode, IReadOnlyList<RecurringAliasInput>? Aliases, IReadOnlyList<RecurringHistoryInput>? History);
public sealed record UpdateRecurringRequest(string Name, string Cadence, DateOnly AnchorDate, decimal ExpectedAmount, string AmountMode, string State, int? ExpectedVersion);
public sealed record RecurringDecisionRequest(Guid TransactionId, DateOnly OccurrenceDate, string Action, int? ExpectedVersion, string? Fingerprint, string? LearnAliasField);
public sealed record RecurringDiscoveryDecisionRequest(string CandidateKey, string Action);
public sealed record RecurringAliasResponse(Guid Id, string Field, string Value);

public sealed record RecurringTransactionEvidence(Guid Id, Guid AccountId, string AccountName, decimal Amount, string Currency,
    DateTimeOffset? PostedAt, string? MerchantName, string? Description, string? Reference, string? Status, string Fingerprint);
public sealed record RecurringSeriesResponse(Guid Id, string Name, Guid AccountId, string AccountName, string Currency, string Cadence,
    DateOnly AnchorDate, decimal ExpectedAmount, string AmountMode, string State, int Version, IReadOnlyList<RecurringAliasResponse> Aliases,
    DateOnly? NextDueDate, string NextDueStatus, int NeedsReviewCount);
public sealed record RecurringCostSummary(string Currency, decimal MonthlyEstimate, decimal AnnualEstimate, int ActiveSeriesCount, int VariableSeriesCount);
public sealed record RecurringSeriesList(IReadOnlyList<RecurringSeriesResponse> Items, IReadOnlyList<RecurringCostSummary> Costs, DateOnly From, DateOnly To);
public sealed record RecurringDiscoveryResponse(string Key, string Name, Guid AccountId, string AccountName, string Currency, string Cadence,
    DateOnly AnchorDate, decimal ExpectedAmount, string AliasField, string AliasValue, IReadOnlyList<RecurringDiscoveryTransaction> Transactions, IReadOnlyList<string> Evidence, bool Dismissed);
public sealed record RecurringDiscoveryTransaction(RecurringTransactionEvidence Snapshot, DateOnly OccurrenceDate);
public sealed record RecurringDiscoveryPage(IReadOnlyList<RecurringDiscoveryResponse> Items, int TotalCount, int Page, int PageSize, DateOnly From, DateOnly To);
public sealed record RecurringOccurrenceResponse(DateOnly Date, DateOnly WindowFrom, DateOnly WindowTo, string Status, decimal ExpectedAmount,
    decimal? PaidAmount, Guid? TransactionId);
public sealed record RecurringOccurrencePage(IReadOnlyList<RecurringOccurrenceResponse> Items, DateOnly From, DateOnly To);
public sealed record RecurringCandidateResponse(RecurringTransactionEvidence Snapshot, DateOnly SuggestedOccurrenceDate, bool AliasMatch,
    bool AmountChanged, IReadOnlyList<string> Evidence, string? DecisionStatus, Guid? CurrentlyAssignedSeriesId, RecurringMatchRanking Ranking);
public sealed record RecurringCandidatePage(IReadOnlyList<RecurringCandidateResponse> Items, int TotalCount, int Page, int PageSize, DateOnly From, DateOnly To);
public sealed record RecurringReviewResponse(Guid Id, Guid TransactionId, DateOnly OccurrenceDate, string Action, RecurringTransactionEvidence Snapshot,
    string CurrentStatus, RecurringTransactionEvidence? CurrentTransaction, string ReviewedByUserId, DateTimeOffset ReviewedAt);
public sealed record RecurringReviewPage(IReadOnlyList<RecurringReviewResponse> Items, int TotalCount, int Page, int PageSize);

public sealed class RecurringConflictException(string message) : Exception(message);
