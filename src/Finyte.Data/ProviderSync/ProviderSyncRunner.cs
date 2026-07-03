using Finyte.Core.ProviderSync;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.ProviderSync;

public interface IProviderSyncRunner
{
    Task Run(Guid syncRunId, CancellationToken cancellationToken);
}

public sealed class ProviderSyncRunner(
    FinyteDbContext dbContext,
    IFiskilBankingSyncService fiskilBankingSyncService,
    ISyncProjectionRefresher syncProjectionRefresher) : IProviderSyncRunner
{
    public async Task Run(Guid syncRunId, CancellationToken cancellationToken)
    {
        var syncRun = await dbContext.ProviderSyncRuns
            .FirstOrDefaultAsync(x => x.Id == syncRunId, cancellationToken)
            ?? throw new InvalidOperationException($"Provider sync run '{syncRunId}' was not found.");

        if (syncRun.Status is ProviderSyncStatus.Succeeded)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        syncRun.Status = ProviderSyncStatus.Running;
        syncRun.StartedAt ??= now;
        syncRun.Error = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var summary = syncRun.Dataset switch
            {
                ProviderSyncDataset.Accounts => await fiskilBankingSyncService.SyncAccounts(syncRun, cancellationToken),
                ProviderSyncDataset.Balances => await fiskilBankingSyncService.SyncBalances(syncRun, cancellationToken),
                ProviderSyncDataset.Transactions => await fiskilBankingSyncService.SyncTransactions(syncRun, cancellationToken),
                _ => throw new InvalidOperationException($"Provider sync dataset '{syncRun.Dataset}' is not supported.")
            };

            await syncProjectionRefresher.RefreshAfterSync(syncRun.Id, summary, cancellationToken);
        }
        catch (Exception exception)
        {
            now = DateTimeOffset.UtcNow;
            syncRun.Status = ProviderSyncStatus.Failed;
            syncRun.Error = exception.Message;
            syncRun.CompletedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
