using System.Data;
using Finyte.Core.Accounts;
using Finyte.Core.PayCycles;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.PayCycles;

public sealed class PayCycleQueries(FinyteDbContext dbContext, TimeProvider timeProvider)
{
    public static IReadOnlyList<string> Kinds { get; } = ["external-credit", "spending", "savings-out", "savings-in", "transfer-out", "transfer-in", "within-scope", "zero"];

    public static PayCycleProfileResponse ToResponse(PayCycleProfile profile) => new(profile.Id, profile.Name,
        profile.Frequency, profile.AnchorDate, profile.Currency, profile.ExpectedIncome, profile.AccountIds,
        profile.SavingsAccountIds, profile.Version, profile.UpdatedAt);

    public async Task<PayCycleBreakdownResponse?> GetBreakdown(Guid tenantId, Guid profileId, DateOnly? date,
        int page, int pageSize, string? kind, CancellationToken cancellationToken)
    {
        // Keep totals, counts and the audit page consistent while ingestion runs concurrently.
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var profile = await dbContext.PayCycleProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var period = PayCycleCalendar.Resolve(profile.Frequency, profile.AnchorDate, date ?? today);
        var observedThrough = period.From > today ? (DateOnly?)null : period.ToExclusive.AddDays(-1) < today ? period.ToExclusive.AddDays(-1) : today;
        var scopeIds = profile.AccountIds.Concat(profile.SavingsAccountIds).Distinct().ToArray();
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId && scopeIds.Contains(x.Id))
            .OrderBy(x => x.CustomName ?? x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var accountIds = accounts.Where(x => profile.AccountIds.Contains(x.Id)).Select(x => x.Id).ToArray();
        var savingsIds = accounts.Where(x => profile.SavingsAccountIds.Contains(x.Id)).Select(x => x.Id).ToArray();
        var from = new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var to = new DateTimeOffset((observedThrough?.AddDays(1) ?? period.From).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var scoped = dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == tenantId && accountIds.Contains(x.AccountId));
        var dated = scoped.Where(x => x.PostedAt >= from && x.PostedAt < to);
        var posted = dated.Where(x => x.Status == null || x.Status == "" || x.Status.ToLower() == "posted");
        var validTransfers = dbContext.ValidConfirmedTransfers(tenantId);
        var query = posted.Where(x => x.Currency == profile.Currency)
            .GroupJoin(validTransfers, x => x.Id, y => y.DebitTransactionId, (x, y) => new { Transaction = x, Debits = y })
            .SelectMany(x => x.Debits.DefaultIfEmpty(), (x, y) => new { x.Transaction, Debit = y })
            .GroupJoin(validTransfers, x => x.Transaction.Id, y => y.CreditTransactionId, (x, y) => new { x.Transaction, x.Debit, Credits = y })
            .SelectMany(x => x.Credits.DefaultIfEmpty(), (x, y) => new TransactionRow
            {
                Id = x.Transaction.Id, AccountId = x.Transaction.AccountId, AccountName = x.Transaction.Account!.CustomName ?? x.Transaction.Account.Name,
                Description = x.Transaction.Description, MerchantName = x.Transaction.MerchantName, Amount = x.Transaction.Amount,
                PostedAt = x.Transaction.PostedAt!.Value,
                Category = x.Transaction.SecondaryCategory != null && x.Transaction.SecondaryCategory.Trim() != "" ? x.Transaction.SecondaryCategory.Trim()
                    : x.Transaction.PrimaryCategory == null || x.Transaction.PrimaryCategory.Trim() == "" ? "Uncategorised" : x.Transaction.PrimaryCategory.Trim(),
                // Confirmed debit/credit IDs are unique, so these joins cannot multiply ledger rows.
                Kind = x.Debit != null && accountIds.Contains(x.Debit.CreditAccountId)
                    || y != null && accountIds.Contains(y.DebitAccountId) ? "within-scope"
                    : x.Debit != null && savingsIds.Contains(x.Debit.CreditAccountId) ? "savings-out"
                    : y != null && savingsIds.Contains(y.DebitAccountId) ? "savings-in"
                    : x.Debit != null ? "transfer-out"
                    : y != null ? "transfer-in"
                    : x.Transaction.Amount > 0 ? "external-credit" : x.Transaction.Amount < 0 ? "spending" : "zero"
            });
        var groups = await query.GroupBy(x => x.Kind)
            .Select(x => new { Kind = x.Key, Amount = x.Sum(y => y.Amount), Count = x.Count() }).ToListAsync(cancellationToken);
        var totals = groups.ToDictionary(x => x.Kind, x => x.Amount);
        var categories = await query.Where(x => x.Kind == "spending").GroupBy(x => x.Category)
            .Select(x => new PayCycleCategory(x.Key, -x.Sum(y => y.Amount), x.Count()))
            .ToListAsync(cancellationToken);
        var filtered = kind is null ? query : query.Where(x => x.Kind == kind);
        var count = await filtered.CountAsync(cancellationToken);
        var rows = await filtered.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var undatedCount = await scoped.CountAsync(x => x.PostedAt == null, cancellationToken);
        var unpostedCount = await dated.CountAsync(x => x.Status != null && x.Status != "" && x.Status.ToLower() != "posted", cancellationToken);
        var currencyCount = await posted.CountAsync(x => x.Currency != profile.Currency, cancellationToken);
        var credits = totals.GetValueOrDefault("external-credit");
        var result = new PayCycleBreakdownResponse(ToResponse(profile), period.From, period.ToExclusive.AddDays(-1),
            observedThrough, NavigationDate(period.From.AddDays(-1)), NavigationDate(period.ToExclusive),
            period.From > today ? "future" : period.ToExclusive <= today ? "completed" : "current", "UTC",
            accounts.Where(x => accountIds.Contains(x.Id)).Select(x => AccountResponse(x)).ToList(),
            accounts.Where(x => savingsIds.Contains(x.Id)).Select(x => AccountResponse(x)).ToList(),
            scopeIds.Except(accounts.Select(x => x.Id)).ToList(),
            new PayCycleTotals(credits, -totals.GetValueOrDefault("spending"), -totals.GetValueOrDefault("savings-out"),
                totals.GetValueOrDefault("savings-in"), -totals.GetValueOrDefault("savings-out") - totals.GetValueOrDefault("savings-in"),
                -totals.GetValueOrDefault("transfer-out"), totals.GetValueOrDefault("transfer-in"), totals.GetValueOrDefault("within-scope"),
                groups.Sum(x => x.Amount), profile.ExpectedIncome is { } expected ? credits - expected : null, groups.Sum(x => x.Count)),
            categories.OrderByDescending(x => x.Amount).ThenBy(x => x.Name).ToList(), undatedCount, unpostedCount, currencyCount,
            new PayCycleTransactionPage(page, pageSize, count, kind, rows.Select(x => new PayCycleTransactionResponse(
                x.Id, x.AccountId, x.AccountName, x.Description, x.MerchantName, x.Amount, x.PostedAt, x.Kind, x.Category)).ToList()));
        if (snapshot is not null)
        {
            await snapshot.CommitAsync(cancellationToken);
        }
        return result;
    }

    private static DateOnly? NavigationDate(DateOnly date) => PayCycleCalendar.ValidDate(date) ? date : null;

    private static PayCycleAccountResponse AccountResponse(Account account) => new(account.Id, AccountPreferences.DisplayName(account), account.Currency, AccountPreferences.IncludeInAnalytics(account));

    private sealed class TransactionRow
    {
        public Guid Id { get; init; }
        public Guid AccountId { get; init; }
        public required string AccountName { get; init; }
        public string? Description { get; init; }
        public string? MerchantName { get; init; }
        public decimal Amount { get; init; }
        public DateTimeOffset PostedAt { get; init; }
        public required string Kind { get; init; }
        public required string Category { get; init; }
    }
}
