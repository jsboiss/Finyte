using System.Text.Json;
using Finyte.Core.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed class ProjectionDispatcher(FinyteDbContext dbContext, IOverviewProjector overviewProjector) : IProjectionDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OverviewResponse> GetOrRebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        var state = await dbContext.ProjectionStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == scope.TenantId
                && x.ProjectionKey == ProjectionKey.Overview
                && x.ScopeKey == scope.ScopeKey,
                cancellationToken);

        if (state is not null && !state.IsStale)
        {
            return await overviewProjector.GetOrRebuild(scope.TenantId, scope.AccountId, cancellationToken);
        }

        return await RebuildOverview(scope, cancellationToken);
    }

    public Task<OverviewResponse> RebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        var scopeJson = JsonSerializer.Serialize(scope, JsonOptions);
        var request = new ProjectionRequest(scope.TenantId, ProjectionKey.Overview, scope.ScopeKey, scopeJson);
        return Run(request, x => overviewProjector.Rebuild(scope, x), cancellationToken);
    }

    public async Task<T> Run<T>(ProjectionRequest request, Func<CancellationToken, Task<T>> rebuild, CancellationToken cancellationToken)
    {
        var state = await GetOrCreateState(request, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        state.Status = ProjectionStatus.Running;
        state.LastError = null;
        state.LastStartedAt = now;
        state.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var response = await rebuild(cancellationToken);
            now = DateTimeOffset.UtcNow;
            state.Status = ProjectionStatus.Succeeded;
            state.IsStale = false;
            state.StaleAt = null;
            state.StaleReason = null;
            state.LastError = null;
            state.LastSucceededAt = now;
            state.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            return response;
        }
        catch (Exception exception)
        {
            now = DateTimeOffset.UtcNow;
            state.Status = ProjectionStatus.Failed;
            state.LastError = exception.Message;
            state.LastFailedAt = now;
            state.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    public async Task MarkTenantStale(Guid tenantId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var states = await dbContext.ProjectionStates
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        foreach (var state in states)
        {
            state.IsStale = true;
            state.StaleAt = now;
            state.StaleReason = reason;
            state.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectionState> GetOrCreateState(ProjectionRequest request, CancellationToken cancellationToken)
    {
        var state = await dbContext.ProjectionStates
            .FirstOrDefaultAsync(x => x.TenantId == request.TenantId
                && x.ProjectionKey == request.ProjectionKey
                && x.ScopeKey == request.ScopeKey,
                cancellationToken);

        if (state is not null)
        {
            state.ScopeJson = request.ScopeJson;
            return state;
        }

        var now = DateTimeOffset.UtcNow;
        state = new ProjectionState
        {
            TenantId = request.TenantId,
            ProjectionKey = request.ProjectionKey,
            ScopeKey = request.ScopeKey,
            ScopeJson = request.ScopeJson,
            Status = ProjectionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ProjectionStates.Add(state);
        return state;
    }
}
