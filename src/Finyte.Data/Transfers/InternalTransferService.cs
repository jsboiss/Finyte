using Finyte.Core.Accounts;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Transfers;

public sealed class InternalTransferService(FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator)
{
    public async Task<TransferReviewPage> GetReview(Guid tenantId, string status, DateOnly from, DateOnly to, int page, CancellationToken cancellationToken)
    {
        var fromTimestamp = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toTimestamp = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var decisions = dbContext.InternalTransfers.AsNoTracking().Where(x => x.TenantId == tenantId);
        List<TransferReview> reviews;
        if (status == "suggested")
        {
            // Keep confirmed legs reserved even when stale, until the user releases or reviews the pair.
            var reserved = decisions.Where(x => x.Status == "confirmed");
            var transactions = await dbContext.Transactions.AsNoTracking().Include(x => x.Account)
                .Where(x => x.TenantId == tenantId && x.PostedAt >= fromTimestamp.AddDays(-3)
                    && x.PostedAt < toTimestamp.AddDays(3) && x.Amount != 0
                    && (x.Status == null || x.Status == "" || x.Status.ToLower() == "posted")
                    && !reserved.Any(y => y.DebitTransactionId == x.Id || y.CreditTransactionId == x.Id))
                .ToListAsync(cancellationToken);
            var candidates = FindCandidates(transactions);
            var transactionIds = transactions.Select(x => x.Id).ToArray();
            var relevantDecisions = await decisions.Where(x => transactionIds.Contains(x.DebitTransactionId) && transactionIds.Contains(x.CreditTransactionId))
                .Select(x => new { x.DebitTransactionId, x.CreditTransactionId }).ToListAsync(cancellationToken);
            var decisionKeys = relevantDecisions.Select(x => (x.DebitTransactionId, x.CreditTransactionId)).ToHashSet();
            var counts = candidates.SelectMany(x => new[] { x.Debit.Id, x.Credit.Id }).GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
            reviews = candidates.Where(x => !decisionKeys.Contains((x.Debit.Id, x.Credit.Id))
                    && x.Debit.PostedAt >= fromTimestamp && x.Debit.PostedAt < toTimestamp)
                .Select(x => new TransferReview(ToLeg(x.Debit), ToLeg(x.Credit), "suggested",
                    counts[x.Debit.Id] > 1 || counts[x.Credit.Id] > 1,
                    "Equal amounts in the same currency, on different family accounts, posted within three days. This does not prove a transfer.", null, null))
                .ToList();
        }
        else
        {
            var valid = dbContext.ValidConfirmedTransfers(tenantId);
            var filtered = status == "needs-review"
                ? decisions.Where(x => x.Status == "confirmed" && !valid.Any(y => y.Id == x.Id))
                : decisions.Where(x => x.Status == status && (status != "confirmed" || valid.Any(y => y.Id == x.Id))
                    && x.DebitPostedAt >= fromTimestamp && x.DebitPostedAt < toTimestamp);
            var count = await filtered.CountAsync(cancellationToken);
            var rows = await filtered.OrderByDescending(x => x.DebitTransaction.PostedAt)
                .ThenBy(x => x.DebitTransactionId).ThenBy(x => x.CreditTransactionId)
                .Skip((page - 1) * 50).Take(50)
                .Include(x => x.DebitTransaction).ThenInclude(x => x.Account)
                .Include(x => x.CreditTransaction).ThenInclude(x => x.Account).ToListAsync(cancellationToken);
            reviews = rows
                .Select(x => new TransferReview(ToLeg(x.DebitTransaction), ToLeg(x.CreditTransaction), status, false,
                    status == "needs-review" ? "A transaction changed after confirmation. Both transactions are included in totals again. Check the details before confirming again."
                    : status == "confirmed" ? "Confirmed by a family member. Both transactions are excluded from spending and income; account balances are unchanged."
                    : "Dismissed by a family member. This pair will not be suggested again unless returned to review.",
                    x.UpdatedAt, x.ReviewedByUserId)).ToList();
            return new TransferReviewPage(reviews, count, page, 50);
        }

        return new TransferReviewPage(reviews.OrderByDescending(x => x.Debit.PostedAt).ThenBy(x => x.Debit.Id).ThenBy(x => x.Credit.Id)
            .Skip((page - 1) * 50).Take(50).ToList(), reviews.Count, page, 50);
    }

    public async Task Review(Guid tenantId, string userId, TransferDecisionRequest request, CancellationToken cancellationToken)
    {
        await using var databaseTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        if (dbContext.Database.IsNpgsql())
        {
            // Serialize decisions within a family, including confirm versus undo and competing pairs.
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE", cancellationToken);
        }
        var transactions = await dbContext.Transactions.Where(x => x.TenantId == tenantId
            && (x.Id == request.DebitTransactionId || x.Id == request.CreditTransactionId)).ToListAsync(cancellationToken);
        var debit = transactions.SingleOrDefault(x => x.Id == request.DebitTransactionId);
        var credit = transactions.SingleOrDefault(x => x.Id == request.CreditTransactionId);
        if (debit is null || credit is null)
        {
            throw new KeyNotFoundException("One of the transactions was not found.");
        }
        var decision = await dbContext.InternalTransfers.SingleOrDefaultAsync(x => x.TenantId == tenantId
            && x.DebitTransactionId == debit.Id && x.CreditTransactionId == credit.Id, cancellationToken);
        var previouslyExcluded = decision is not null && await dbContext.ValidConfirmedTransfers(tenantId)
            .AnyAsync(x => x.Id == decision.Id, cancellationToken);
        if (request.Action == "reset")
        {
            if (decision is not null)
            {
                dbContext.InternalTransfers.Remove(decision);
            }
        }
        else
        {
            if (!IsCandidate(debit, credit))
            {
                throw new InvalidOperationException("These transactions no longer match. Refresh the review list.");
            }
            // Reject a stale screen even if both amounts were corrected to a new matching value.
            if (request.Amount != credit.Amount || request.Currency != credit.Currency
                || request.DebitPostedAt != debit.PostedAt || request.CreditPostedAt != credit.PostedAt
                || request.DebitAccountId != debit.AccountId || request.CreditAccountId != credit.AccountId)
            {
                throw new InvalidOperationException("The transaction details changed. Refresh and review them again.");
            }
            var occupied = await dbContext.InternalTransfers.AnyAsync(x => x.TenantId == tenantId && x.Status == "confirmed"
                && (decision == null || x.Id != decision.Id)
                && (x.DebitTransactionId == debit.Id || x.CreditTransactionId == debit.Id
                    || x.DebitTransactionId == credit.Id || x.CreditTransactionId == credit.Id), cancellationToken);
            if (occupied)
            {
                throw new InvalidOperationException("A transaction is already linked to another confirmed transfer. Return that pair to review first.");
            }
            if (decision is null)
            {
                decision = new InternalTransfer
                {
                    TenantId = tenantId, DebitTransactionId = debit.Id, CreditTransactionId = credit.Id,
                    Status = "dismissed", Currency = credit.Currency, ReviewedByUserId = userId
                };
                dbContext.InternalTransfers.Add(decision);
            }
            decision.Status = request.Action == "confirm" ? "confirmed" : "dismissed";
            decision.Amount = credit.Amount;
            decision.Currency = credit.Currency;
            decision.DebitAccountId = debit.AccountId;
            decision.CreditAccountId = credit.AccountId;
            decision.DebitPostedAt = debit.PostedAt!.Value;
            decision.CreditPostedAt = credit.PostedAt!.Value;
            decision.ReviewedByUserId = userId;
            decision.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (previouslyExcluded != (request.Action == "confirm"))
        {
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "internal transfer reviewed", cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
    }

    public static IReadOnlyList<TransferCandidate> FindCandidates(IReadOnlyList<Transaction> transactions)
    {
        var credits = transactions.Where(x => x.Amount > 0 && IsPosted(x)).ToLookup(x => (x.TenantId, x.Amount, x.Currency));
        var candidates = new List<TransferCandidate>();
        foreach (var debit in transactions.Where(x => x.Amount < 0 && IsPosted(x)))
        {
            foreach (var credit in credits[(debit.TenantId, -debit.Amount, debit.Currency)])
            {
                if (IsCandidate(debit, credit))
                {
                    candidates.Add(new TransferCandidate(debit, credit));
                }
            }
        }
        return candidates;
    }

    private static bool IsCandidate(Transaction debit, Transaction credit)
    {
        return IsPosted(debit) && IsPosted(credit) && debit.TenantId == credit.TenantId
            && debit.AccountId != credit.AccountId && debit.Amount < 0 && credit.Amount == -debit.Amount
            && debit.Currency == credit.Currency
            && Math.Abs((debit.PostedAt!.Value.UtcDateTime.Date - credit.PostedAt!.Value.UtcDateTime.Date).Days) <= 3;
    }

    private static bool IsPosted(Transaction transaction)
    {
        return transaction.PostedAt.HasValue && (string.IsNullOrEmpty(transaction.Status)
            || string.Equals(transaction.Status, "posted", StringComparison.OrdinalIgnoreCase));
    }

    private static TransferLeg ToLeg(Transaction transaction) => new(transaction.Id, transaction.AccountId,
        transaction.Account is null ? "Account" : AccountPreferences.DisplayName(transaction.Account), transaction.Description ?? "Transaction", transaction.Amount, transaction.Currency, transaction.PostedAt);
}

public sealed record TransferCandidate(Transaction Debit, Transaction Credit);
public sealed record TransferLeg(Guid Id, Guid AccountId, string AccountName, string Description, decimal Amount, string Currency, DateTimeOffset? PostedAt);
public sealed record TransferReview(TransferLeg Debit, TransferLeg Credit, string Status, bool IsAmbiguous, string Explanation, DateTimeOffset? ReviewedAt, string? ReviewedByUserId);
public sealed record TransferReviewPage(IReadOnlyList<TransferReview> Items, int TotalCount, int Page, int PageSize);
public sealed record TransferDecisionRequest(Guid DebitTransactionId, Guid CreditTransactionId, string Action,
    decimal Amount, string Currency, Guid DebitAccountId, Guid CreditAccountId, DateTimeOffset? DebitPostedAt, DateTimeOffset? CreditPostedAt);
