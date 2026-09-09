using Finyte.Core.Accounts;

namespace Finyte.Data.Transfers;

public static class InternalTransferQueries
{
    // A provider correction must not keep hiding transactions based on an obsolete decision.
    public static IQueryable<InternalTransfer> ValidConfirmedTransfers(this FinyteDbContext dbContext, Guid tenantId)
    {
        return dbContext.InternalTransfers.Where(x => x.TenantId == tenantId && x.Status == "confirmed"
            && x.DebitTransaction.TenantId == tenantId && x.CreditTransaction.TenantId == tenantId
            && x.DebitTransaction.AccountId == x.DebitAccountId && x.CreditTransaction.AccountId == x.CreditAccountId
            && x.DebitAccountId != x.CreditAccountId && x.Amount > 0
            && x.DebitTransaction.Amount == -x.Amount && x.CreditTransaction.Amount == x.Amount
            && x.DebitTransaction.Currency == x.Currency && x.CreditTransaction.Currency == x.Currency
            && x.DebitTransaction.PostedAt == x.DebitPostedAt && x.CreditTransaction.PostedAt == x.CreditPostedAt
            && (x.DebitTransaction.Status == null || x.DebitTransaction.Status == "" || x.DebitTransaction.Status.ToLower() == "posted")
            && (x.CreditTransaction.Status == null || x.CreditTransaction.Status == "" || x.CreditTransaction.Status.ToLower() == "posted"));
    }

    public static IQueryable<Transaction> ExcludeInternalTransfers(this IQueryable<Transaction> transactions, FinyteDbContext dbContext, Guid tenantId)
    {
        var confirmed = dbContext.ValidConfirmedTransfers(tenantId);
        return transactions.Where(x => !confirmed.Any(y => y.DebitTransactionId == x.Id || y.CreditTransactionId == x.Id));
    }
}
