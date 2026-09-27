using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Scheduling;
using Finyte.Data.Tenancy;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed class OverviewProjector(FinyteDbContext dbContext, TenantCalendars calendars) : IOverviewProjector
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public async Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        var monthKey = calendar.CurrentMonthKey;
        var projection = await dbContext.OverviewProjections
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AccountId == accountId && x.MonthKey == monthKey)
            .OrderByDescending(x => x.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is not null)
        {
            return Deserialize(projection.PayloadJson);
        }

        return await Rebuild(new OverviewProjectionScope(tenantId, accountId, monthKey), cancellationToken);
    }

    public Task<OverviewResponse> Rebuild(OverviewProjectionScope scope, CancellationToken cancellationToken) => Build(scope, false, cancellationToken);

    // The comparison view must never overwrite the default transfer-excluding projection.
    public Task<OverviewResponse> ReadIncludingTransfers(OverviewProjectionScope scope, CancellationToken cancellationToken) => Build(scope, true, cancellationToken);

    public Task<OverviewResponse> ReadAccountSet(OverviewProjectionScope scope, bool includeInternalTransfers, CancellationToken cancellationToken) =>
        Build(scope, includeInternalTransfers, cancellationToken);

    private async Task<OverviewResponse> Build(OverviewProjectionScope scope, bool includeInternalTransfers, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var calendar = await calendars.For(scope.TenantId, cancellationToken);
        var timeZoneId = calendar.TimeZoneId;
        var sourceVersion = await dbContext.Tenants
            .Where(x => x.Id == scope.TenantId)
            .Select(x => x.FinancialDataVersion)
            .SingleAsync(cancellationToken);
        var month = ParseMonthKey(scope.MonthKey);
        var today = calendar.Today;
        var elapsedDays = scope.MonthKey == calendar.CurrentMonthKey
            ? Math.Max(1, today.Day)
            : DateTime.DaysInMonth(month.Year, month.Month);
        var monthStart = calendar.StartOf(new DateOnly(month.Year, month.Month, 1));
        var nextMonthStart = calendar.StartOf(new DateOnly(month.Year, month.Month, 1).AddMonths(1));

        var accountRows = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.TenantId == scope.TenantId && (scope.AccountId == null || x.Id == scope.AccountId)
                && (scope.AccountIds == null || scope.AccountIds.Contains(x.Id)))
            .OrderBy(x => x.CustomName ?? x.Name).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var explicitScope = scope.AccountId != null || scope.AccountIds != null;

        // Account preferences control all-account income/spending, never balances or direct inspection.
        var accountIds = accountRows.Where(x => explicitScope || AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToList();
        var currency = AccountPreferences.AnalyticsCurrency(accountRows, explicitScope ? accountRows.FirstOrDefault()?.Id : null);
        var currencyAccounts = accountRows.Where(x => x.Currency == currency).ToList();
        var reportedBalances = currencyAccounts.Where(x => AccountPreferences.HasReportedBalance(x)).ToList();
        var accountBalanceMinorUnits = reportedBalances.Sum(x => ToMinorUnits(x.CurrentBalance));
        var balanceCoverage = new OverviewBalanceCoverageResponse(
            reportedBalances.Count,
            currencyAccounts.Count,
            currencyAccounts.Where(x => !AccountPreferences.HasReportedBalance(x)).Select(AccountPreferences.DisplayName).ToList());
        var accountLabel = scope.AccountIds is not null ? scope.Label ?? $"{accountRows.Count} accounts"
            : scope.AccountId is null
            ? "All accounts"
            : accountRows.FirstOrDefault() is { } account ? AccountPreferences.DisplayName(account) : "Selected account";

        var monthQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(x => x.TenantId == scope.TenantId
                && accountIds.Contains(x.AccountId)
                && x.PostedAt >= monthStart
                && x.PostedAt < nextMonthStart
                && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"));
        if (!includeInternalTransfers)
        {
            monthQuery = monthQuery.ExcludeInternalTransfers();
        }

        var transactionQuery = monthQuery.Where(x => x.Currency == currency);
        var excludedCurrencies = await monthQuery
            .Where(x => x.Currency != currency)
            .GroupBy(x => x.Currency)
            .Select(x => new { Currency = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var currencyScope = new OverviewCurrencyScopeResponse(
            accountRows.Count - currencyAccounts.Count,
            excludedCurrencies.Sum(x => x.Count),
            excludedCurrencies.Select(x => x.Currency).OrderBy(x => x, StringComparer.Ordinal).ToList());
        var totals = await transactionQuery
            .GroupBy(x => 1)
            .Select(x => new TransactionTotals(
                x.Sum(y => y.Amount > 0 ? y.Amount : 0),
                x.Sum(y => y.Amount < 0 ? -y.Amount : 0)))
            .FirstOrDefaultAsync(cancellationToken) ?? new TransactionTotals(0, 0);
        var incomeMinorUnits = ToMinorUnits(totals.Income);
        var expenseMinorUnits = ToMinorUnits(totals.Expense);
        var averageDailySpendMinorUnits = expenseMinorUnits / elapsedDays;
        var dailyMap = Enumerable.Range(1, DateTime.DaysInMonth(month.Year, month.Month))
            .Select(x => new OverviewDailyCashFlowAccumulator(new DateOnly(month.Year, month.Month, x)))
            .ToDictionary(x => x.Date);
        var dailyTotals = await transactionQuery
            .GroupBy(x => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.PostedAt!.Value.UtcDateTime, timeZoneId).Date)
            .Select(x => new DailyTransactionTotals(
                x.Key,
                x.Sum(y => y.Amount > 0 ? y.Amount : 0),
                x.Sum(y => y.Amount < 0 ? -y.Amount : 0)))
            .ToListAsync(cancellationToken);

        foreach (var dailyTotal in dailyTotals)
        {
            if (!dailyMap.TryGetValue(DateOnly.FromDateTime(dailyTotal.Date), out var day))
            {
                continue;
            }

            day.IncomeMinorUnits = ToMinorUnits(dailyTotal.Income);
            day.ExpenseMinorUnits = ToMinorUnits(dailyTotal.Expense);
        }

        var dailyCashFlow = dailyMap.Values
            .OrderBy(x => x.Date)
            .Select(x => new OverviewDailyCashFlowResponse(x.Date.ToString("yyyy-MM-dd"), x.Date.Day, x.IncomeMinorUnits, x.ExpenseMinorUnits))
            .ToList();
        var expenseRows = await transactionQuery
            .Where(x => x.Amount < 0)
            .Select(x => new TransactionRow(
                x.Amount,
                x.TagAssignments
                    .OrderBy(y => y.Tag == null ? "" : y.Tag.Name)
                    .Select(y => new TagRow(y.TagId, y.Tag == null ? "Untagged" : y.Tag.Name, y.Tag == null ? "#94a3b8" : y.Tag.Color))
                    .ToList()))
            .ToListAsync(cancellationToken);
        var tagSpend = new Dictionary<string, TagSpendAccumulator>();
        foreach (var transaction in expenseRows)
        {
            var amount = Math.Abs(ToMinorUnits(transaction.Amount));
            var tags = transaction.Tags.Count == 0
                ? [new TagRow(null, "Untagged", "#94a3b8")]
                : transaction.Tags;
            var splitAmount = amount / tags.Count;
            var remainder = amount % tags.Count;

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                var tagKey = tag.Id?.ToString("N") ?? "untagged";
                if (!tagSpend.TryGetValue(tagKey, out var accumulator))
                {
                    accumulator = new TagSpendAccumulator(tag.Id, tag.Name, tag.Color);
                    tagSpend[tagKey] = accumulator;
                }

                accumulator.AmountMinorUnits += splitAmount + (i == 0 ? remainder : 0);
            }
        }

        var monthlySpendByTag = tagSpend
            .OrderByDescending(x => x.Value.AmountMinorUnits)
            .Select(x => new OverviewMonthlySpendByTagResponse(
                x.Value.Id,
                x.Value.Name,
                x.Value.Color,
                x.Value.AmountMinorUnits,
                expenseMinorUnits > 0 ? Math.Round((decimal)x.Value.AmountMinorUnits / expenseMinorUnits * 100, 1) : 0))
            .DefaultIfEmpty(new OverviewMonthlySpendByTagResponse(null, "Untagged", "#94a3b8", 0, 0))
            .ToList();
        var transactionWatermark = await transactionQuery
            .Select(x => (DateTimeOffset?)(x.PostedAt ?? x.CreatedAt))
            .MaxAsync(cancellationToken);
        var accountWatermark = currencyAccounts
            .Select(x => (DateTimeOffset?)(x.BalanceAsOf ?? x.CreatedAt))
            .Max();
        var sourceWatermark = transactionWatermark is null || accountWatermark > transactionWatermark
            ? accountWatermark
            : transactionWatermark;
        var response = new OverviewResponse(
            new OverviewScopeResponse(scope.AccountId, accountLabel, scope.AccountIds),
            scope.MonthKey,
            currency,
            accountBalanceMinorUnits,
            expenseMinorUnits,
            averageDailySpendMinorUnits,
            new OverviewCashFlowRaceResponse(incomeMinorUnits, expenseMinorUnits, incomeMinorUnits - expenseMinorUnits),
            dailyCashFlow,
            monthlySpendByTag,
            new OverviewFreshnessResponse(now, sourceWatermark, IsRefreshing: false),
            balanceCoverage,
            currencyScope);
        if (includeInternalTransfers || scope.AccountIds is not null)
        {
            return response;
        }
        var payloadJson = JsonSerializer.Serialize(response, JsonOptions);
        var existingProjection = await dbContext.OverviewProjections
            .FirstOrDefaultAsync(x => x.TenantId == scope.TenantId && x.AccountId == scope.AccountId && x.MonthKey == scope.MonthKey, cancellationToken);

        if (existingProjection is null)
        {
            dbContext.OverviewProjections.Add(new OverviewProjection
            {
                TenantId = scope.TenantId,
                AccountId = scope.AccountId,
                MonthKey = scope.MonthKey,
                Currency = currency,
                PayloadJson = payloadJson,
                SourceVersion = sourceVersion,
                Status = ProjectionStatus.Succeeded,
                SourceWatermark = sourceWatermark,
                CalculatedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existingProjection.Currency = currency;
            existingProjection.PayloadJson = payloadJson;
            existingProjection.SourceVersion = sourceVersion;
            existingProjection.Status = ProjectionStatus.Succeeded;
            existingProjection.LastError = null;
            existingProjection.InvalidatedAt = null;
            existingProjection.SourceWatermark = sourceWatermark;
            existingProjection.CalculatedAt = now;
            existingProjection.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return response;
    }

    private static OverviewResponse Deserialize(string payloadJson)
    {
        return JsonSerializer.Deserialize<OverviewResponse>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Overview projection payload could not be deserialized.");
    }

    private static YearMonth ParseMonthKey(string monthKey)
    {
        if (monthKey.Length != 7
            || monthKey[4] != '-'
            || !int.TryParse(monthKey[..4], out var year)
            || !int.TryParse(monthKey[5..], out var month)
            || month is < 1 or > 12)
        {
            throw new ArgumentException("Month key must use yyyy-MM format.", nameof(monthKey));
        }

        return new YearMonth(year, month);
    }

    private static long ToMinorUnits(decimal amount)
    {
        return (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
    }

    private sealed record TransactionRow(decimal Amount, IReadOnlyList<TagRow> Tags);

    private sealed record TransactionTotals(decimal Income, decimal Expense);

    private sealed record DailyTransactionTotals(DateTime Date, decimal Income, decimal Expense);

    private sealed record TagRow(Guid? Id, string Name, string Color);

    private sealed record YearMonth(int Year, int Month);

    private sealed class OverviewDailyCashFlowAccumulator(DateOnly date)
    {
        public DateOnly Date { get; } = date;

        public long IncomeMinorUnits { get; set; }

        public long ExpenseMinorUnits { get; set; }
    }

    private sealed class TagSpendAccumulator(Guid? id, string name, string color)
    {
        public Guid? Id { get; } = id;

        public string Name { get; } = name;

        public string Color { get; } = color;

        public long AmountMinorUnits { get; set; }
    }
}
