using Finyte.Core.Budgets;
using Finyte.Data;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Api.Development;

public static class SandboxFinancialReset
{
    // The caller owns the transaction so clearing data and queuing the replacement sync commit together.
    public static async Task Clear(FinyteDbContext dbContext, Guid tenantId, string consentId, CancellationToken cancellationToken)
    {
        await Delete(dbContext, dbContext.RecurringPaymentReviews.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.RecurringPaymentDecisions.Where(x => x.TenantId == tenantId), cancellationToken);
        var seriesIds = dbContext.RecurringPaymentSeries.Where(x => x.TenantId == tenantId).Select(x => x.Id);
        await Delete(dbContext, dbContext.RecurringPaymentAliases.Where(x => seriesIds.Contains(x.SeriesId)), cancellationToken);
        await Delete(dbContext, dbContext.RecurringPaymentSeries.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.RecurringDiscoveryDecisions.Where(x => x.TenantId == tenantId), cancellationToken);
        var transactionIds = dbContext.Transactions.Where(x => x.TenantId == tenantId).Select(x => x.Id);
        await Delete(dbContext, dbContext.TransactionTagAssignments.Where(x => transactionIds.Contains(x.TransactionId)), cancellationToken);
        await Delete(dbContext, dbContext.TransactionTagExclusions.Where(x => transactionIds.Contains(x.TransactionId)), cancellationToken);
        await Delete(dbContext, dbContext.TransactionFileIdentities.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.TransactionFileImports.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.Transactions.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.OverviewProjections.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.PayCycleProfiles.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.ProviderWebhookEvents.Where(x => x.TenantId == tenantId), cancellationToken);
        await Delete(dbContext, dbContext.ProviderSyncRuns.Where(x => x.TenantId == tenantId), cancellationToken);

        // Keep sandbox identities and any accounts referenced by budgets. Deleting these would
        // silently change the scope of retained budgets or break it on every subsequent reset.
        var budgetAccountIds = dbContext.Set<BudgetAccount>().Where(x => x.Budget.TenantId == tenantId).Select(x => x.AccountId);
        await Delete(dbContext, dbContext.Accounts.Where(x => x.TenantId == tenantId && x.ConsentId != consentId && !budgetAccountIds.Contains(x.Id)), cancellationToken);
        var accounts = await dbContext.Accounts.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            account.CurrentBalance = 0;
            account.AvailableBalance = null;
            account.CreditLimit = null;
            account.BalanceAsOf = null;
            account.ManualBalanceVersion++;
        }
        var tenant = await dbContext.Tenants.SingleAsync(x => x.Id == tenantId, cancellationToken);
        tenant.FinancialDataVersion++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task Delete<T>(FinyteDbContext dbContext, IQueryable<T> query, CancellationToken cancellationToken) where T : class
    {
        if (dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            dbContext.RemoveRange(await query.ToListAsync(cancellationToken));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
