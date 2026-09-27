using Finyte.Core.Accounts;

namespace Finyte.Data.Transfers;

public static class InternalTransferQueries
{
    public static IQueryable<Transaction> ExcludeInternalTransfers(this IQueryable<Transaction> transactions)
    {
        return transactions.Where(x => x.InternalTransferAccountId == null);
    }
}
