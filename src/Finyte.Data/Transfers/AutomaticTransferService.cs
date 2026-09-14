using System.Text.RegularExpressions;
using Finyte.Core.Accounts;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Transfers;

public sealed partial class AutomaticTransferService(FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator)
{
    public static string Reviewer => "system:automatic-transfer-v1";

    public async Task<int> Reconcile(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsNpgsql())
        {
            var lockKey = $"transaction-tagging:{tenantId:N}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE", cancellationToken);
        }
        var decisions = await dbContext.InternalTransfers.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var manualReservations = decisions.Where(x => x.ReviewedByUserId != Reviewer && (x.Status == "confirmed" || x.Status == "needs-review"))
            .SelectMany(x => new[] { x.DebitTransactionId, x.CreditTransactionId }).ToHashSet();
        var rows = await dbContext.Transactions.AsNoTracking().Where(x => x.TenantId == tenantId && x.Account != null && x.Account.TenantId == tenantId
            && x.PostedAt != null && x.Amount != 0 && !manualReservations.Contains(x.Id)).ToListAsync(cancellationToken);
        var candidates = InternalTransferService.FindCandidates(rows);
        // Count all equal-amount candidates, including those without transfer words. Never choose a winner in a tie.
        var counts = candidates.SelectMany(x => new[] { x.Debit.Id, x.Credit.Id }).GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var eligible = candidates.Where(x => counts[x.Debit.Id] == 1 && counts[x.Credit.Id] == 1 && HasEvidence(x.Debit, x.Credit))
            .ToDictionary(x => (x.Debit.Id, x.Credit.Id));
        var changed = 0;
        foreach (var decision in decisions.Where(x => x.ReviewedByUserId == Reviewer && x.Status == "confirmed"))
        {
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
