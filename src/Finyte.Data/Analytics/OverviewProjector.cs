using System.Text.Json;
using Finyte.Core.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed class OverviewProjector(FinyteDbContext dbContext) : IOverviewProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
    {
        var monthKey = GetCurrentMonthKey();
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

    public async Task<OverviewResponse> Rebuild(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var month = ParseMonthKey(scope.MonthKey);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var currentMonthKey = GetCurrentMonthKey();
        var elapsedDays = scope.MonthKey == currentMonthKey
            ? Math.Max(1, today.Day)
            : DateTime.DaysInMonth(month.Year, month.Month);
        var monthStart = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonthStart = monthStart.AddMonths(1);

        var accountRows = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.TenantId == scope.TenantId && (scope.AccountId == null || x.Id == scope.AccountId))
            .OrderBy(x => x.Name)
            .Select(x => new AccountRow(x.Id, x.Name, x.CurrentBalance, x.Currency, x.BalanceAsOf, x.CreatedAt))
            .ToListAsync(cancellationToken);

        var accountIds = accountRows.Select(x => x.Id).ToList();
        var currency = accountRows.Select(x => x.Currency).FirstOrDefault() ?? "AUD";
        var accountBalanceMinorUnits = accountRows.Sum(x => ToMinorUnits(x.CurrentBalance));
        var accountLabel = scope.AccountId is null
            ? "All accounts"
            : accountRows.FirstOrDefault()?.Name ?? "Selected account";

        var transactionRows = accountIds.Count == 0
            ? new List<TransactionRow>()
            : await dbContext.Transactions
                .AsNoTracking()
                .Where(x => x.TenantId == scope.TenantId
                    && accountIds.Contains(x.AccountId)
                    && x.PostedAt >= monthStart
                    && x.PostedAt < nextMonthStart
                    && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"))
                .Select(x => new TransactionRow(x.Amount, x.PostedAt, x.CreatedAt))
                .ToListAsync(cancellationToken);

        var incomeMinorUnits = transactionRows
            .Where(x => x.Amount > 0)
            .Sum(x => ToMinorUnits(x.Amount));
        var expenseMinorUnits = transactionRows
            .Where(x => x.Amount < 0)
            .Sum(x => Math.Abs(ToMinorUnits(x.Amount)));
        var averageDailySpendMinorUnits = expenseMinorUnits / elapsedDays;
        var dailyMap = Enumerable.Range(1, DateTime.DaysInMonth(month.Year, month.Month))
            .Select(x => new OverviewDailyCashFlowAccumulator(new DateOnly(month.Year, month.Month, x)))
            .ToDictionary(x => x.Date);

        foreach (var transaction in transactionRows)
        {
            if (transaction.PostedAt is null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(transaction.PostedAt.Value.UtcDateTime);
            if (!dailyMap.TryGetValue(date, out var day))
            {
                continue;
            }

            var amount = ToMinorUnits(transaction.Amount);
            if (amount > 0)
            {
                day.IncomeMinorUnits += amount;
            }
            else
            {
                day.ExpenseMinorUnits += Math.Abs(amount);
            }
        }

        var dailyCashFlow = dailyMap.Values
            .OrderBy(x => x.Date)
            .Select(x => new OverviewDailyCashFlowResponse(x.Date.ToString("yyyy-MM-dd"), x.Date.Day, x.IncomeMinorUnits, x.ExpenseMinorUnits))
            .ToList();
        var monthlySpendByTag = new List<OverviewMonthlySpendByTagResponse>
        {
            new(null, "Untagged", "#94a3b8", expenseMinorUnits, expenseMinorUnits > 0 ? 100 : 0)
        };
        var sourceWatermark = transactionRows
            .Select(x => x.PostedAt ?? x.CreatedAt)
            .Concat(accountRows.Select(x => x.BalanceAsOf ?? x.CreatedAt))
            .DefaultIfEmpty()
            .Max();
        var response = new OverviewResponse(
            new OverviewScopeResponse(scope.AccountId, accountLabel),
            scope.MonthKey,
            currency,
            accountBalanceMinorUnits,
            expenseMinorUnits,
            averageDailySpendMinorUnits,
            new OverviewCashFlowRaceResponse(incomeMinorUnits, expenseMinorUnits, incomeMinorUnits - expenseMinorUnits),
            dailyCashFlow,
            monthlySpendByTag,
            new OverviewFreshnessResponse(now, sourceWatermark == default ? null : sourceWatermark, IsRefreshing: false));
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
                SourceWatermark = sourceWatermark == default ? null : sourceWatermark,
                CalculatedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existingProjection.Currency = currency;
            existingProjection.PayloadJson = payloadJson;
            existingProjection.SourceWatermark = sourceWatermark == default ? null : sourceWatermark;
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

    private static string GetCurrentMonthKey()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return $"{today.Year:D4}-{today.Month:D2}";
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

    private sealed record AccountRow(Guid Id, string Name, decimal CurrentBalance, string Currency, DateTimeOffset? BalanceAsOf, DateTimeOffset CreatedAt);

    private sealed record TransactionRow(decimal Amount, DateTimeOffset? PostedAt, DateTimeOffset CreatedAt);

    private sealed record YearMonth(int Year, int Month);

    private sealed class OverviewDailyCashFlowAccumulator(DateOnly date)
    {
        public DateOnly Date { get; } = date;

        public long IncomeMinorUnits { get; set; }

        public long ExpenseMinorUnits { get; set; }
    }
}
