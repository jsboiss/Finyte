using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProjectionDispatcherTests
{
    [Fact]
    public async Task SuccessfulRebuildRecordsProjectionStateSuccess()
    {
        await using var dbContext = CreateDbContext();
        var dispatcher = new ProjectionDispatcher(dbContext, new SuccessfulOverviewProjector());
        var scope = new OverviewProjectionScope(Guid.NewGuid(), AccountId: null, "2026-06");

        await dispatcher.RebuildOverview(scope, CancellationToken.None);

        var state = await dbContext.ProjectionStates.SingleAsync();
        Assert.Equal(scope.TenantId, state.TenantId);
        Assert.Equal(ProjectionKey.Overview, state.ProjectionKey);
        Assert.Equal(scope.ScopeKey, state.ScopeKey);
        Assert.Equal(ProjectionStatus.Succeeded, state.Status);
        Assert.NotNull(state.LastStartedAt);
        Assert.NotNull(state.LastSucceededAt);
        Assert.Null(state.LastFailedAt);
        Assert.Null(state.LastError);
        Assert.False(state.IsStale);
        Assert.Null(state.StaleAt);
        Assert.Null(state.StaleReason);
    }

    [Fact]
    public async Task SuccessfulRebuildClearsProjectionStateStaleMarker()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var scope = new OverviewProjectionScope(tenantId, AccountId: null, "2026-06");
        dbContext.ProjectionStates.Add(new ProjectionState
        {
            TenantId = tenantId,
            ProjectionKey = ProjectionKey.Overview,
            ScopeKey = scope.ScopeKey,
            ScopeJson = "{}",
            Status = ProjectionStatus.Succeeded,
            IsStale = true,
            StaleAt = DateTimeOffset.UtcNow,
            StaleReason = "tag rule changed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var dispatcher = new ProjectionDispatcher(dbContext, new SuccessfulOverviewProjector());

        await dispatcher.RebuildOverview(scope, CancellationToken.None);

        var state = await dbContext.ProjectionStates.SingleAsync();
        Assert.Equal(ProjectionStatus.Succeeded, state.Status);
        Assert.False(state.IsStale);
        Assert.Null(state.StaleAt);
        Assert.Null(state.StaleReason);
    }

    [Fact]
    public async Task MarkTenantStaleMarksOnlyTenantProjectionStates()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        dbContext.ProjectionStates.Add(CreateState(tenantId, "scope-1"));
        dbContext.ProjectionStates.Add(CreateState(tenantId, "scope-2"));
        dbContext.ProjectionStates.Add(CreateState(otherTenantId, "scope-3"));
        await dbContext.SaveChangesAsync();

        var dispatcher = new ProjectionDispatcher(dbContext, new SuccessfulOverviewProjector());

        await dispatcher.MarkTenantStale(tenantId, "merchant tag rule changed", CancellationToken.None);

        var tenantStates = await dbContext.ProjectionStates
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.ScopeKey)
            .ToListAsync();
        Assert.All(tenantStates, x =>
        {
            Assert.True(x.IsStale);
            Assert.NotNull(x.StaleAt);
            Assert.Equal("merchant tag rule changed", x.StaleReason);
        });

        var otherTenantState = await dbContext.ProjectionStates.SingleAsync(x => x.TenantId == otherTenantId);
        Assert.False(otherTenantState.IsStale);
        Assert.Null(otherTenantState.StaleAt);
        Assert.Null(otherTenantState.StaleReason);
    }

    [Fact]
    public async Task GetOrRebuildOverviewRebuildsStaleProjectionState()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var scope = new OverviewProjectionScope(tenantId, AccountId: null, "2026-06");
        dbContext.ProjectionStates.Add(new ProjectionState
        {
            TenantId = tenantId,
            ProjectionKey = ProjectionKey.Overview,
            ScopeKey = scope.ScopeKey,
            ScopeJson = "{}",
            Status = ProjectionStatus.Succeeded,
            IsStale = true,
            StaleAt = DateTimeOffset.UtcNow,
            StaleReason = "tenant data changed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var dispatcher = new ProjectionDispatcher(dbContext, new SuccessfulOverviewProjector());

        await dispatcher.GetOrRebuildOverview(scope, CancellationToken.None);

        var state = await dbContext.ProjectionStates.SingleAsync();
        Assert.Equal(ProjectionStatus.Succeeded, state.Status);
        Assert.False(state.IsStale);
        Assert.Null(state.StaleAt);
        Assert.Null(state.StaleReason);
    }

    [Fact]
    public async Task FailedRebuildRecordsProjectionStateFailureWithoutChangingRawData()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var account = new Account
        {
            TenantId = tenantId,
            Name = "Everyday",
            CurrentBalance = 123.45m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();

        var dispatcher = new ProjectionDispatcher(dbContext, new FailingOverviewProjector());
        var scope = new OverviewProjectionScope(tenantId, AccountId: null, "2026-06");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.RebuildOverview(scope, CancellationToken.None));

        var state = await dbContext.ProjectionStates.SingleAsync();
        Assert.Equal(ProjectionStatus.Failed, state.Status);
        Assert.NotNull(state.LastStartedAt);
        Assert.NotNull(state.LastFailedAt);
        Assert.Contains("projection failure", state.LastError);
        Assert.Equal(1, await dbContext.Accounts.CountAsync());
        Assert.Equal(123.45m, await dbContext.Accounts.Select(x => x.CurrentBalance).SingleAsync());
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FinyteDbContext(options);
    }

    private static ProjectionState CreateState(Guid tenantId, string scopeKey)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProjectionState
        {
            TenantId = tenantId,
            ProjectionKey = ProjectionKey.Overview,
            ScopeKey = scopeKey,
            ScopeJson = "{}",
            Status = ProjectionStatus.Succeeded,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class SuccessfulOverviewProjector : IOverviewProjector
    {
        public Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResponse(new OverviewProjectionScope(tenantId, accountId, DateTimeOffset.UtcNow.ToString("yyyy-MM"))));
        }

        public Task<OverviewResponse> Rebuild(OverviewProjectionScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateResponse(scope));
        }

        private static OverviewResponse CreateResponse(OverviewProjectionScope scope)
        {
            return new OverviewResponse(
                new OverviewScopeResponse(scope.AccountId, "Test"),
                scope.MonthKey,
                "AUD",
                0,
                0,
                0,
                new OverviewCashFlowRaceResponse(0, 0, 0),
                [],
                [],
                new OverviewFreshnessResponse(DateTimeOffset.UtcNow, SourceWatermark: null, IsRefreshing: false));
        }
    }

    private sealed class FailingOverviewProjector : IOverviewProjector
    {
        public Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("projection failure");
        }

        public Task<OverviewResponse> Rebuild(OverviewProjectionScope scope, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("projection failure");
        }
    }
}
