using Finyte.Core.Analytics;
using Finyte.Data.Analytics;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProjectionInvalidatorTests
{
    [Fact]
    public async Task TransactionChangedRebuildsAccountAndAllOverviewScopes()
    {
        var dispatcher = new RecordingProjectionDispatcher();
        var invalidator = new ProjectionInvalidator(dispatcher);
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var postedAt = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

        await invalidator.TransactionChanged(tenantId, accountId, postedAt, CancellationToken.None);

        Assert.Collection(dispatcher.Scopes,
            x => Assert.Equal(new OverviewProjectionScope(tenantId, accountId, "2026-06"), x),
            x => Assert.Equal(new OverviewProjectionScope(tenantId, AccountId: null, "2026-06"), x));
    }

    [Fact]
    public async Task AccountBalanceChangedRebuildsCurrentAccountAndAllOverviewScopes()
    {
        var dispatcher = new RecordingProjectionDispatcher();
        var invalidator = new ProjectionInvalidator(dispatcher);
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var monthKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");

        await invalidator.AccountBalanceChanged(tenantId, accountId, CancellationToken.None);

        Assert.Collection(dispatcher.Scopes,
            x => Assert.Equal(new OverviewProjectionScope(tenantId, accountId, monthKey), x),
            x => Assert.Equal(new OverviewProjectionScope(tenantId, AccountId: null, monthKey), x));
    }

    [Fact]
    public async Task TenantProjectionDataChangedMarksTenantStale()
    {
        var dispatcher = new RecordingProjectionDispatcher();
        var invalidator = new ProjectionInvalidator(dispatcher);
        var tenantId = Guid.NewGuid();

        await invalidator.TenantProjectionDataChanged(tenantId, "bulk tag rule changed", CancellationToken.None);

        Assert.Equal(tenantId, dispatcher.StaleTenantId);
        Assert.Equal("bulk tag rule changed", dispatcher.StaleReason);
    }

    private sealed class RecordingProjectionDispatcher : IProjectionDispatcher
    {
        public List<OverviewProjectionScope> Scopes { get; } = [];
        public Guid? StaleTenantId { get; private set; }
        public string? StaleReason { get; private set; }

        public Task<T> Run<T>(ProjectionRequest request, Func<CancellationToken, Task<T>> rebuild, CancellationToken cancellationToken)
        {
            return rebuild(cancellationToken);
        }

        public Task<OverviewResponse> GetOrRebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
        {
            return RebuildOverview(scope, cancellationToken);
        }

        public Task<OverviewResponse> RebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            return Task.FromResult(new OverviewResponse(
                new OverviewScopeResponse(scope.AccountId, "Test"),
                scope.MonthKey,
                "AUD",
                0,
                0,
                0,
                new OverviewCashFlowRaceResponse(0, 0, 0),
                [],
                [],
                new OverviewFreshnessResponse(DateTimeOffset.UtcNow, SourceWatermark: null, IsRefreshing: false)));
        }

        public Task MarkTenantStale(Guid tenantId, string reason, CancellationToken cancellationToken)
        {
            StaleTenantId = tenantId;
            StaleReason = reason;
            return Task.CompletedTask;
        }
    }
}
