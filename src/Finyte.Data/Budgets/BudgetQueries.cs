using Finyte.Core.Accounts;
using Finyte.Core.Budgets;
using Finyte.Core.Scheduling;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Budgets;

public static class BudgetQueries
{
    public static async Task<IQueryable<Transaction>> Transactions(FinyteDbContext dbContext, Budget budget, FinancialCalendar calendar, CancellationToken cancellationToken, bool includeOtherCurrencies = false)
    {
        var selectedIds = budget.Accounts.Select(x => x.AccountId).ToArray();
        var accounts = await dbContext.Accounts.AsNoTracking()
            .Where(x => x.TenantId == budget.TenantId && (budget.AccountScope != "selected" || selectedIds.Contains(x.Id)))
            .ToListAsync(cancellationToken);
        var accountIds = accounts.Where(x => budget.AccountScope == "selected" || AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToArray();
        var observedUntil = calendar.EndExclusive(calendar.Today);
        var query = dbContext.Transactions.AsNoTracking()
            .Where(x => x.TenantId == budget.TenantId && accountIds.Contains(x.AccountId)
                && (includeOtherCurrencies || x.Currency == budget.Currency) && x.Amount < 0 && x.PostedAt != null && x.PostedAt < observedUntil
                && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"))
            .ExcludeInternalTransfers(dbContext, budget.TenantId);
        if (budget.MatchMode == "selected")
        {
            var categories = budget.Categories.Select(x => x.ToLowerInvariant()).ToArray();
            var tagIds = budget.Tags.Select(x => x.TagId).ToArray();
            query = query.Where(x => categories.Contains((x.PrimaryCategory ?? "").Trim().ToLower())
                || categories.Contains((x.SecondaryCategory ?? "").Trim().ToLower())
                || x.TagAssignments.Any(y => tagIds.Contains(y.TagId) && y.Tag != null && y.Tag.TenantId == budget.TenantId));
        }
        return query;
    }

    public static IQueryable<Transaction> InPeriod(this IQueryable<Transaction> query, BudgetPeriod period, FinancialCalendar calendar)
    {
        var from = calendar.StartOf(period.From);
        var to = calendar.StartOf(period.EndExclusive);
        return query.Where(x => x.PostedAt >= from && x.PostedAt < to);
    }
}
