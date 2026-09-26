using Finyte.Core.Accounts;
using Finyte.Core.Budgets;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Budgets;

public static class BudgetQueries
{
    public static async Task<IQueryable<Transaction>> Transactions(FinyteDbContext dbContext, Budget budget, CancellationToken cancellationToken, bool includeOtherCurrencies = false)
    {
        var selectedIds = budget.Accounts.Select(x => x.AccountId).ToArray();
        var accounts = await dbContext.Accounts.AsNoTracking()
            .Where(x => x.TenantId == budget.TenantId && (budget.AccountScope != "selected" || selectedIds.Contains(x.Id)))
            .ToListAsync(cancellationToken);
        var accountIds = accounts.Where(x => budget.AccountScope == "selected" || AccountPreferences.IncludeInAnalytics(x)).Select(x => x.Id).ToArray();
        var observedUntil = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);
        var query = dbContext.Transactions.AsNoTracking()
            .Where(x => x.TenantId == budget.TenantId && accountIds.Contains(x.AccountId)
                && (includeOtherCurrencies || x.Currency == budget.Currency) && x.Amount < 0 && x.PostedAt != null && x.PostedAt < observedUntil
                && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted"))
            .ExcludeInternalTransfers(dbContext, budget.TenantId);
        if (budget.MatchMode == "selected")
        {
            var categories = budget.Categories.Select(x => x.ToLowerInvariant()).ToArray();
            var tagIds = budget.Tags.Select(x => x.TagId).ToArray();
            query = query.Where(x => (x.CategoryOverride != null && x.CategoryOverride.Trim() != ""
                    ? categories.Contains(x.CategoryOverride.Trim().ToLower())
                    : (x.PrimaryCategory ?? "").Trim() != "" || (x.SecondaryCategory ?? "").Trim() != ""
                        ? categories.Contains((x.PrimaryCategory ?? "").Trim().ToLower())
                            || categories.Contains((x.SecondaryCategory ?? "").Trim().ToLower())
                        : categories.Contains((x.CategoryFromRule ?? "").Trim().ToLower()))
                || x.TagAssignments.Any(y => tagIds.Contains(y.TagId) && y.Tag != null && y.Tag.TenantId == budget.TenantId));
        }
        return query;
    }

    public static IQueryable<Transaction> InPeriod(this IQueryable<Transaction> query, BudgetPeriod period)
    {
        var from = new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(period.EndExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return query.Where(x => x.PostedAt >= from && x.PostedAt < to);
    }
}
