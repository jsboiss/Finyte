using System.Text.Json;
using Finyte.Core.ProviderSync;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.ProviderSync;

public sealed record SyncChangeSummary(
    IReadOnlyCollection<Guid> AccountIds,
    DateTimeOffset? MinChangedAt,
    DateTimeOffset? MaxChangedAt,
    bool HasChanges)
{
    public static SyncChangeSummary Empty { get; } = new([], MinChangedAt: null, MaxChangedAt: null, HasChanges: false);
}

public interface ISyncProjectionRefresher
{
    Task RefreshAfterSync(Guid syncRunId, SyncChangeSummary changeSummary, CancellationToken cancellationToken);
}

public sealed class SyncProjectionRefresher(
    FinyteDbContext dbContext,
    IProjectionInvalidator projectionInvalidator) : ISyncProjectionRefresher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RefreshAfterSync(Guid syncRunId, SyncChangeSummary changeSummary, CancellationToken cancellationToken)
    {
        var syncRun = await dbContext.ProviderSyncRuns.FirstOrDefaultAsync(x => x.Id == syncRunId, cancellationToken)
            ?? throw new InvalidOperationException($"Sync run '{syncRunId}' was not found.");

        var now = DateTimeOffset.UtcNow;
        syncRun.Status = ProviderSyncStatus.Succeeded;
        syncRun.CompletedAt ??= now;
        syncRun.ChangeSummaryJson = JsonSerializer.Serialize(changeSummary, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hasPendingSync = await dbContext.ProviderSyncRuns
            .AnyAsync(x => x.TenantId == syncRun.TenantId
                && x.Id != syncRun.Id
                && (x.Status == ProviderSyncStatus.Queued || x.Status == ProviderSyncStatus.Running),
                cancellationToken);

        if (hasPendingSync)
        {
            return;
        }

        var syncRuns = await dbContext.ProviderSyncRuns
            .Where(x => x.TenantId == syncRun.TenantId
                && x.Status == ProviderSyncStatus.Succeeded
                && x.ProjectionRefreshedAt == null)
            .OrderBy(x => x.CompletedAt)
            .ToListAsync(cancellationToken);

        if (syncRuns.Count == 0)
        {
            return;
        }

        await new Transfers.AutomaticTransferService(dbContext, projectionInvalidator).Reconcile(syncRun.TenantId, cancellationToken);
        var summary = MergeSummaries(syncRuns);

        if (summary.HasChanges)
        {
            await RefreshProjections(syncRun.TenantId, syncRuns.Select(x => x.Dataset).Distinct().ToList(), summary, cancellationToken);
        }

        now = DateTimeOffset.UtcNow;
        foreach (var run in syncRuns)
        {
            run.ProjectionRefreshedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshProjections(Guid tenantId, IReadOnlyCollection<string> datasets, SyncChangeSummary summary, CancellationToken cancellationToken)
    {
        if (summary.AccountIds.Count > 10 || datasets.Count > 1)
        {
            await projectionInvalidator.TenantProjectionDataChanged(tenantId, "provider sync changed broad tenant data", cancellationToken);
            return;
        }

        foreach (var accountId in summary.AccountIds)
        {
            if (datasets.Contains(ProviderSyncDataset.Balances))
            {
                await projectionInvalidator.AccountBalanceChanged(tenantId, accountId, cancellationToken);
            }

            if (datasets.Contains(ProviderSyncDataset.Transactions))
            {
                await projectionInvalidator.TransactionChanged(tenantId, accountId, summary.MaxChangedAt, cancellationToken);
            }

            if (datasets.Contains(ProviderSyncDataset.Accounts))
            {
                await projectionInvalidator.TenantProjectionDataChanged(tenantId, "provider account sync changed account data", cancellationToken);
                return;
            }
        }
    }

    private static SyncChangeSummary MergeSummaries(IReadOnlyCollection<ProviderSyncRun> syncRuns)
    {
        var summaries = syncRuns
            .Select(x => string.IsNullOrWhiteSpace(x.ChangeSummaryJson)
                ? SyncChangeSummary.Empty
                : JsonSerializer.Deserialize<SyncChangeSummary>(x.ChangeSummaryJson, JsonOptions) ?? SyncChangeSummary.Empty)
            .ToList();

        var accountIds = summaries
            .SelectMany(x => x.AccountIds)
            .Distinct()
            .ToList();
        var minChangedAt = summaries
            .Where(x => x.MinChangedAt is not null)
            .Select(x => x.MinChangedAt)
            .Min();
        var maxChangedAt = summaries
            .Where(x => x.MaxChangedAt is not null)
            .Select(x => x.MaxChangedAt)
            .Max();

        return new SyncChangeSummary(
            accountIds,
            minChangedAt,
            maxChangedAt,
            summaries.Any(x => x.HasChanges));
    }
}
