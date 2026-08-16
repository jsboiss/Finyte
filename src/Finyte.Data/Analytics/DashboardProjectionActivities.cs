using Finyte.Core.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Activities;

namespace Finyte.Data.Analytics;

public sealed class DashboardProjectionActivities(IServiceScopeFactory serviceScopeFactory)
{
    [Activity]
    public async Task Rebuild(Guid projectionId)
    {
        ActivityExecutionContext.Current.Heartbeat();
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinyteDbContext>();
        var projection = await dbContext.OverviewProjections
            .SingleOrDefaultAsync(x => x.Id == projectionId, ActivityExecutionContext.Current.CancellationToken);

        if (projection is null)
        {
            return;
        }

        projection.Status = ProjectionStatus.Running;
        projection.LastError = null;
        projection.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ActivityExecutionContext.Current.CancellationToken);

        var dispatcher = scope.ServiceProvider.GetRequiredService<IProjectionDispatcher>();
        await dispatcher.RebuildOverview(
            new OverviewProjectionScope(projection.TenantId, projection.AccountId, projection.MonthKey),
            ActivityExecutionContext.Current.CancellationToken);
        ActivityExecutionContext.Current.Heartbeat();
    }
}
