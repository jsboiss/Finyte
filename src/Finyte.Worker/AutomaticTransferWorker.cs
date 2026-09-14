using Finyte.Data;
using Finyte.Data.Transfers;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Worker;

public sealed class AutomaticTransferWorker(IServiceScopeFactory scopeFactory, ILogger<AutomaticTransferWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
                var tenantIds = await dbContext.Tenants.AsNoTracking().Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        await using var tenantScope = scopeFactory.CreateAsyncScope();
                        await tenantScope.ServiceProvider.GetRequiredService<AutomaticTransferService>().Reconcile(tenantId, stoppingToken);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning(exception, "Automatic transfer reconciliation will retry for tenant {TenantId}", tenantId);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Automatic transfer reconciliation will retry");
            }
            // Imports and syncs reconcile their changed windows immediately; this
            // slower sweep revalidates historical decisions and account changes.
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
