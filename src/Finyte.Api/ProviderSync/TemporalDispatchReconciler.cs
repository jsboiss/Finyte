using Finyte.Core.ProviderSync;
using Finyte.Core.Analytics;
using Finyte.Data;
using Finyte.Data.Temporal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finyte.Api.ProviderSync;

public sealed class TemporalDispatchReconciler(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<TemporalOptions> options,
    ILogger<TemporalDispatchReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Temporal provider sync dispatch is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                await DispatchPending(stoppingToken);
                await DispatchPendingProjections(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to dispatch queued provider sync workflows to Temporal.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchPending(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ITemporalProviderSyncDispatcher>();
        var batchIds = await dbContext.ProviderSyncRuns
            .AsNoTracking()
            .Where(x => x.Status == ProviderSyncStatus.Queued && x.DispatchedAt == null)
            .GroupBy(x => x.BatchId)
            .OrderBy(x => x.Min(y => y.CreatedAt))
            .Take(10)
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        foreach (var batchId in batchIds)
        {
            var syncRunIds = await dbContext.ProviderSyncRuns
                .AsNoTracking()
                .Where(x => x.BatchId == batchId && x.Status == ProviderSyncStatus.Queued)
                .OrderBy(x => x.Dataset == ProviderSyncDataset.Accounts ? 0 : x.Dataset == ProviderSyncDataset.Balances ? 1 : 2)
                .ThenBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            if (syncRunIds.Count == 0)
            {
                continue;
            }

            var workflowId = await dispatcher.Dispatch(batchId, syncRunIds, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var syncRuns = await dbContext.ProviderSyncRuns
                .Where(x => x.BatchId == batchId && x.DispatchedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var syncRun in syncRuns)
            {
                syncRun.TemporalWorkflowId = workflowId;
                syncRun.DispatchedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DispatchPendingProjections(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ITemporalDashboardDispatcher>();
        var projections = await dbContext.OverviewProjections
            .Where(x => x.Status == ProjectionStatus.Pending && x.DispatchedAt == null)
            .OrderBy(x => x.InvalidatedAt)
            .Take(25)
            .Select(x => new PendingProjection(
                x.Id,
                x.Generation))
            .ToListAsync(cancellationToken);

        foreach (var projection in projections)
        {
            var workflowId = await dispatcher.Dispatch(projection.Id, projection.Generation, cancellationToken);
            var updated = await dbContext.OverviewProjections
                .Where(x => x.Id == projection.Id && x.Status == ProjectionStatus.Pending && x.DispatchedAt == null)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.TemporalWorkflowId, workflowId)
                    .SetProperty(y => y.DispatchedAt, DateTimeOffset.UtcNow),
                    cancellationToken);

            if (updated == 0)
            {
                logger.LogDebug("Projection {ProjectionId} changed before its Temporal dispatch was recorded.", projection.Id);
            }
        }
    }

    private sealed record PendingProjection(Guid Id, long Generation);
}
