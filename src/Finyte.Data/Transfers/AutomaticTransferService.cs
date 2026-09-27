using System.Text.RegularExpressions;
using Finyte.Core.Accounts;
using Finyte.Data.Tenancy;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Transfers;

public sealed partial class AutomaticTransferService(FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator, TenantCalendars calendars)
{
    public static string Reviewer => "system:automatic-transfer-v1";

    public async Task<int> Reconcile(Guid tenantId, CancellationToken cancellationToken, DateOnly? from = null, DateOnly? to = null)
    {
        if (from.HasValue != to.HasValue || from > to)
        {
            throw new ArgumentException("Supply both ends of the changed transaction range in date order.");
        }
        // A changed row can affect a leg three days away, its partner another three
        // days away, and that partner's competitors another three days away.
        var calendar = await calendars.For(tenantId, cancellationToken);
        var focusFrom = from.HasValue ? calendar.StartOf(Shift(from.Value, -3)) : DateTimeOffset.MinValue;
        var focusTo = to.HasValue ? calendar.EndExclusive(Shift(to.Value, 3)) : DateTimeOffset.MaxValue;
        var loadFrom = from.HasValue ? calendar.StartOf(Shift(from.Value, -9)) : DateTimeOffset.MinValue;
        var loadTo = to.HasValue ? calendar.EndExclusive(Shift(to.Value, 9)) : DateTimeOffset.MaxValue;
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsNpgsql())
        {
            // Imports and provider upserts also hold this lock. Keep their ledger
            // writes serialized with uniqueness checks, in tagging-then-tenant order.
            var lockKey = $"transaction-tagging:{tenantId:N}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE", cancellationToken);
        }
        var rowQuery = dbContext.Transactions.Where(x => x.TenantId == tenantId && x.Account != null && x.Account.TenantId == tenantId
            && x.PostedAt != null && x.PostedAt >= loadFrom && x.PostedAt < loadTo && x.Amount != 0);
        var decisionsQuery = dbContext.InternalTransfers.Where(x => x.TenantId == tenantId);
        if (from.HasValue)
        {
            // Include snapshots even when a corrected row moved or became unposted.
            decisionsQuery = decisionsQuery.Where(x => rowQuery.Any(y => y.Id == x.DebitTransactionId || y.Id == x.CreditTransactionId)
                || (x.DebitPostedAt >= focusFrom && x.DebitPostedAt < focusTo)
                || (x.CreditPostedAt >= focusFrom && x.CreditPostedAt < focusTo));
        }
        var decisions = await decisionsQuery.ToListAsync(cancellationToken);
        var manualReservations = decisions.Where(x => x.ReviewedByUserId != Reviewer && (x.Status == "confirmed" || x.Status == "needs-review"))
            .SelectMany(x => new[] { x.DebitTransactionId, x.CreditTransactionId }).ToHashSet();
        var rows = await rowQuery.AsNoTracking().Where(x => !manualReservations.Contains(x.Id)).ToListAsync(cancellationToken);
        var focusIds = rows.Where(x => x.PostedAt >= focusFrom && x.PostedAt < focusTo).Select(x => x.Id).ToHashSet();
        var candidates = InternalTransferService.FindCandidates(rows, calendar);
        // Count all equal-amount candidates, including those without transfer words. Never choose a winner in a tie.
        var counts = candidates.SelectMany(x => new[] { x.Debit.Id, x.Credit.Id }).GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var eligible = candidates.Where(x => counts[x.Debit.Id] == 1 && counts[x.Credit.Id] == 1 && HasEvidence(x.Debit, x.Credit))
            .ToDictionary(x => (x.Debit.Id, x.Credit.Id));
        var changed = 0;
        foreach (var decision in decisions.Where(x => x.ReviewedByUserId == Reviewer && x.Status == "confirmed"))
        {
            if (from.HasValue && !focusIds.Contains(decision.DebitTransactionId) && !focusIds.Contains(decision.CreditTransactionId)
                && !(decision.DebitPostedAt >= focusFrom && decision.DebitPostedAt < focusTo)
                && !(decision.CreditPostedAt >= focusFrom && decision.CreditPostedAt < focusTo))
            {
                continue;
            }
            if (!eligible.TryGetValue((decision.DebitTransactionId, decision.CreditTransactionId), out var pair)
                || pair.Credit.Amount != decision.Amount || pair.Debit.PostedAt != decision.DebitPostedAt
                || pair.Credit.PostedAt != decision.CreditPostedAt || pair.Credit.Currency != decision.Currency
                || pair.Debit.AccountId != decision.DebitAccountId || pair.Credit.AccountId != decision.CreditAccountId)
            {
                decision.Status = "needs-review";
                decision.UpdatedAt = DateTimeOffset.UtcNow;
                changed++;
            }
        }
        var reviewed = decisions.Select(x => (x.DebitTransactionId, x.CreditTransactionId)).ToHashSet();
        var reserved = decisions.Where(x => x.Status is "confirmed" or "needs-review")
            .SelectMany(x => new[] { x.DebitTransactionId, x.CreditTransactionId }).ToHashSet();
        foreach (var pair in eligible.Values)
        {
            if (!focusIds.Contains(pair.Debit.Id) && !focusIds.Contains(pair.Credit.Id))
            {
                continue;
            }
            if (reviewed.Contains((pair.Debit.Id, pair.Credit.Id)) || reserved.Contains(pair.Debit.Id) || reserved.Contains(pair.Credit.Id))
            {
                continue;
            }
            dbContext.InternalTransfers.Add(new InternalTransfer
            {
                TenantId = tenantId, DebitTransactionId = pair.Debit.Id, CreditTransactionId = pair.Credit.Id,
                DebitAccountId = pair.Debit.AccountId, CreditAccountId = pair.Credit.AccountId,
                DebitPostedAt = pair.Debit.PostedAt!.Value, CreditPostedAt = pair.Credit.PostedAt!.Value,
                Amount = pair.Credit.Amount, Currency = pair.Credit.Currency, Status = "confirmed",
                ReviewedByUserId = Reviewer, UpdatedAt = DateTimeOffset.UtcNow
            });
            changed++;
        }
        if (changed > 0)
        {
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "automatic transfers reconciled", cancellationToken);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return changed;
    }

    private static DateOnly Shift(DateOnly date, int offset)
    {
        var day = Math.Clamp((long)date.DayNumber + offset, DateOnly.MinValue.DayNumber, DateOnly.MaxValue.DayNumber);
        return DateOnly.FromDayNumber((int)day);
    }

    public static bool HasEvidence(Transaction debit, Transaction credit)
    {
        var sharedReference = !string.IsNullOrWhiteSpace(debit.Reference) && debit.Reference.Trim().Length >= 6
            && string.Equals(debit.Reference.Trim(), credit.Reference?.Trim(), StringComparison.OrdinalIgnoreCase);
        var bothMarked = TransferWords().IsMatch(debit.Description ?? "") && TransferWords().IsMatch(credit.Description ?? "");
        return bothMarked || (sharedReference && (TransferWords().IsMatch(debit.Description ?? "") || TransferWords().IsMatch(credit.Description ?? "")));
    }

    [GeneratedRegex(@"\b(transfer|xfer|tfr)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransferWords();
}
