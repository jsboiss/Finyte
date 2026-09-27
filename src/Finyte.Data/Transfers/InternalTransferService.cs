using Finyte.Core.Accounts;
using Finyte.Core.Scheduling;
using Finyte.Core.Transfers;
using Finyte.Data.Tenancy;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Transfers;

public sealed class InternalTransferService(FinyteDbContext dbContext, IProjectionInvalidator projectionInvalidator, TenantCalendars calendars)
{
    public static IReadOnlyList<string> Views { get; } = ["transfers", "excluded"];

    public async Task<TransferReviewPage> GetReview(Guid tenantId, string view, DateOnly from, DateOnly to, int page, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        var fromTimestamp = calendar.StartOf(from);
        var toTimestamp = calendar.EndExclusive(to);
        var query = dbContext.Transactions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PostedAt >= fromTimestamp && x.PostedAt < toTimestamp);
        query = view == "excluded"
            ? query.Where(x => x.InternalTransferSource == InternalTransferDetector.Excluded)
            : query.Where(x => x.InternalTransferAccountId != null);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 50).Take(50)
            .Include(x => x.Account).Include(x => x.InternalTransferAccount)
            .ToListAsync(cancellationToken);
        return new TransferReviewPage(rows.Select(x => ToRow(x, calendar)).ToList(), count, page, 50);
    }

    public async Task Review(Guid tenantId, TransferDecisionRequest request, CancellationToken cancellationToken)
    {
        await using var databaseTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.TransactionId, cancellationToken)
            ?? throw new KeyNotFoundException("The transaction was not found.");
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var previousAccountId = transaction.InternalTransferAccountId;
        var ruleAdded = false;
        switch (request.Action)
        {
            case "mark":
                var counterparty = accounts.SingleOrDefault(x => x.Id == request.CounterpartyAccountId)
                    ?? throw new KeyNotFoundException("The other account was not found.");
                if (counterparty.Id == transaction.AccountId)
                {
                    throw new InvalidOperationException("A transfer needs a different account on the other side.");
                }
                transaction.InternalTransferAccountId = counterparty.Id;
                transaction.InternalTransferSource = InternalTransferDetector.Manual;
                ruleAdded = request.CreateRule != false && await AddRule(counterparty.Id, transaction, cancellationToken);
                break;
            case "exclude":
                transaction.InternalTransferAccountId = null;
                transaction.InternalTransferSource = InternalTransferDetector.Excluded;
                break;
            default:
                transaction.InternalTransferAccountId = null;
                transaction.InternalTransferSource = null;
                InternalTransferDetector.Apply(transaction, accounts);
                break;
        }
        if (transaction.InternalTransferAccountId != previousAccountId)
        {
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "internal transfer reviewed", cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (ruleAdded)
        {
            await Reclassify(tenantId, cancellationToken);
        }
        if (databaseTransaction is not null)
        {
            await databaseTransaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<bool> AddRule(Guid counterpartyId, Transaction transaction, CancellationToken cancellationToken)
    {
        var phrase = InternalTransferDetector.RulePhrase(transaction.Description);
        if (phrase is null)
        {
            return false;
        }
        var counterparty = await dbContext.Accounts.SingleAsync(x => x.Id == counterpartyId, cancellationToken);
        var nicknames = InternalTransferDetector.MergeRule(counterparty.TransferNicknames, phrase);
        if (nicknames is null)
        {
            return false;
        }
        counterparty.TransferNicknames = nicknames;
        counterparty.PreferencesVersion++;
        return true;
    }

    public async Task<int> Reclassify(Guid tenantId, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var changed = 0;
        await foreach (var transaction in dbContext.Transactions
            .Where(x => x.TenantId == tenantId && (x.InternalTransferSource == null || x.InternalTransferSource == InternalTransferDetector.Detected))
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (InternalTransferDetector.Apply(transaction, accounts))
            {
                changed++;
            }
        }
        if (changed > 0)
        {
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "internal transfers reclassified", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return changed;
    }

    private static TransferRow ToRow(Transaction transaction, FinancialCalendar calendar) => new(transaction.Id, transaction.AccountId,
        transaction.Account is null ? "Account" : AccountPreferences.DisplayName(transaction.Account),
        transaction.Description ?? "Transaction", transaction.Amount, transaction.Currency, transaction.PostedAt,
        transaction.PostedAt is { } postedAt ? calendar.ToDate(postedAt) : null, transaction.InternalTransferAccountId,
        transaction.InternalTransferAccount is null ? null : AccountPreferences.DisplayName(transaction.InternalTransferAccount),
        transaction.InternalTransferSource);
}

public sealed record TransferRow(Guid Id, Guid AccountId, string AccountName, string Description, decimal Amount, string Currency,
    DateTimeOffset? PostedAt, DateOnly? PostedDate, Guid? CounterpartyAccountId, string? CounterpartyAccountName, string? Source);
public sealed record TransferReviewPage(IReadOnlyList<TransferRow> Items, int TotalCount, int Page, int PageSize);
public sealed record TransferDecisionRequest(Guid TransactionId, string Action, Guid? CounterpartyAccountId, bool? CreateRule = null);
