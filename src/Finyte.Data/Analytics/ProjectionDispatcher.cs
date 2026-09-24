using System.Text.Json;
using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed class ProjectionDispatcher(FinyteDbContext dbContext, IOverviewProjector overviewProjector) : IProjectionDispatcher
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static TimeSpan FailedRetryCooldown { get; } = TimeSpan.FromMinutes(1);

    public async Task<OverviewResponse> GetOrRebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        var sourceVersion = await dbContext.Tenants
            .Where(x => x.Id == scope.TenantId)
            .Select(x => x.FinancialDataVersion)
            .SingleAsync(cancellationToken);
        var projection = await dbContext.OverviewProjections
            .FirstOrDefaultAsync(x => x.TenantId == scope.TenantId
                && x.AccountId == scope.AccountId
                && x.MonthKey == scope.MonthKey,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (projection is null)
        {
            projection = await CreatePendingProjection(scope, cancellationToken);
        }
        else if (NeedsRebuild(projection, scope, sourceVersion, now))
        {
            MarkPending(projection, now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var response = Deserialize(projection.PayloadJson);
        var isStale = projection.SourceVersion < sourceVersion || IsElapsedDaysStale(projection, scope, now);
        var isRefreshing = projection.Status is ProjectionStatus.Pending or ProjectionStatus.Running || isStale;
        return response with
        {
            Freshness = response.Freshness with
            {
                IsRefreshing = isRefreshing,
                IsStale = isStale,
                HasFailed = projection.Status == ProjectionStatus.Failed,
                LastError = projection.Status == ProjectionStatus.Failed ? projection.LastError : null
            }
        };
    }

    private static bool NeedsRebuild(OverviewProjection projection, OverviewProjectionScope scope, long sourceVersion, DateTimeOffset now)
    {
        if (projection.Status == ProjectionStatus.Failed)
        {
            return projection.UpdatedAt + FailedRetryCooldown <= now;
        }

        if (projection.Status != ProjectionStatus.Succeeded)
        {
            return false;
        }

        return projection.SourceVersion < sourceVersion || IsElapsedDaysStale(projection, scope, now);
    }

    private static bool IsElapsedDaysStale(OverviewProjection projection, OverviewProjectionScope scope, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return scope.MonthKey == $"{today.Year:D4}-{today.Month:D2}"
            && DateOnly.FromDateTime(projection.CalculatedAt.UtcDateTime) < today;
    }

    public async Task<OverviewResponse> RebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        try
        {
            return await overviewProjector.Rebuild(scope, cancellationToken);
        }
        catch (Exception exception)
        {
            var projection = await dbContext.OverviewProjections
                .FirstOrDefaultAsync(x => x.TenantId == scope.TenantId
                    && x.AccountId == scope.AccountId
                    && x.MonthKey == scope.MonthKey,
                    cancellationToken);

            if (projection is not null)
            {
                projection.Status = ProjectionStatus.Failed;
                projection.LastError = exception.Message;
                projection.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task<OverviewProjection> CreatePendingProjection(OverviewProjectionScope scope, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var label = scope.AccountId is null
            ? "All accounts"
            : await dbContext.Accounts
                .Where(x => x.TenantId == scope.TenantId && x.Id == scope.AccountId)
                .Select(x => x.CustomName ?? x.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? "Selected account";
        var accounts = await dbContext.Accounts.AsNoTracking()
            .Where(x => x.TenantId == scope.TenantId && (scope.AccountId == null || x.Id == scope.AccountId))
            .ToListAsync(cancellationToken);
        var currency = AccountPreferences.AnalyticsCurrency(accounts, scope.AccountId);
        var response = new OverviewResponse(
            new OverviewScopeResponse(scope.AccountId, label),
            scope.MonthKey,
            currency,
            0,
            0,
            0,
            new OverviewCashFlowRaceResponse(0, 0, 0),
            [],
            [new OverviewMonthlySpendByTagResponse(null, "Untagged", "#94a3b8", 0, 0)],
            new OverviewFreshnessResponse(now, null, IsRefreshing: true));
        var projection = new OverviewProjection
        {
            TenantId = scope.TenantId,
            AccountId = scope.AccountId,
            MonthKey = scope.MonthKey,
            Currency = currency,
            PayloadJson = JsonSerializer.Serialize(response, JsonOptions),
            SourceVersion = -1,
            Generation = 1,
            Status = ProjectionStatus.Pending,
            CalculatedAt = now,
            InvalidatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.OverviewProjections.Add(projection);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return projection;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(projection).State = EntityState.Detached;
            return await dbContext.OverviewProjections.SingleAsync(x => x.TenantId == scope.TenantId
                && x.AccountId == scope.AccountId
                && x.MonthKey == scope.MonthKey,
                cancellationToken);
        }
    }

    private static void MarkPending(OverviewProjection projection, DateTimeOffset now)
    {
        projection.Generation++;
        projection.Status = ProjectionStatus.Pending;
        projection.LastError = null;
        projection.TemporalWorkflowId = null;
        projection.DispatchedAt = null;
        projection.InvalidatedAt = now;
        projection.UpdatedAt = now;
    }

    private static OverviewResponse Deserialize(string payloadJson)
    {
        return JsonSerializer.Deserialize<OverviewResponse>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Overview projection payload could not be deserialized.");
    }
}
