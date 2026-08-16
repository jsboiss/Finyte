using System.Text.Json;
using Finyte.Core.Analytics;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProjectionDispatcherTests
{
    [Fact]
    public async Task CurrentSnapshotReturnsWithoutRebuilding()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant(financialDataVersion: 3);
        var projection = CreateProjection(tenant.Id, sourceVersion: 3);
        dbContext.AddRange(tenant, projection);
        await dbContext.SaveChangesAsync();
        var projector = new RecordingOverviewProjector();
        var dispatcher = new ProjectionDispatcher(dbContext, projector);

        var response = await dispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(tenant.Id, null, projection.MonthKey), CancellationToken.None);

        Assert.False(response.Freshness.IsRefreshing);
        Assert.Equal(0, projector.RebuildCount);
    }

    [Fact]
    public async Task StaleSnapshotReturnsImmediatelyAndQueuesRefresh()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant(financialDataVersion: 4);
        var projection = CreateProjection(tenant.Id, sourceVersion: 3);
        dbContext.AddRange(tenant, projection);
        await dbContext.SaveChangesAsync();
        var projector = new RecordingOverviewProjector();
        var dispatcher = new ProjectionDispatcher(dbContext, projector);

        var response = await dispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(tenant.Id, null, projection.MonthKey), CancellationToken.None);

        Assert.True(response.Freshness.IsRefreshing);
        Assert.Equal(0, projector.RebuildCount);
        Assert.Equal(ProjectionStatus.Pending, projection.Status);
        Assert.Null(projection.DispatchedAt);
    }

    [Fact]
    public async Task MissingSnapshotCreatesPendingPlaceholderWithoutRebuilding()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant(financialDataVersion: 1);
        dbContext.Add(tenant);
        await dbContext.SaveChangesAsync();
        var projector = new RecordingOverviewProjector();
        var dispatcher = new ProjectionDispatcher(dbContext, projector);

        var response = await dispatcher.GetOrRebuildOverview(
            new OverviewProjectionScope(tenant.Id, null, "2026-06"), CancellationToken.None);

        Assert.True(response.Freshness.IsRefreshing);
        Assert.Equal(0, projector.RebuildCount);
        Assert.Equal(ProjectionStatus.Pending, (await dbContext.OverviewProjections.SingleAsync()).Status);
    }

    [Fact]
    public async Task FailedRebuildRecordsFailureOnExistingSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant(financialDataVersion: 1);
        var projection = CreateProjection(tenant.Id, sourceVersion: 0);
        dbContext.AddRange(tenant, projection);
        await dbContext.SaveChangesAsync();
        var dispatcher = new ProjectionDispatcher(dbContext, new FailingOverviewProjector());

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.RebuildOverview(
            new OverviewProjectionScope(tenant.Id, null, projection.MonthKey), CancellationToken.None));

        Assert.Equal(ProjectionStatus.Failed, projection.Status);
        Assert.Contains("projection failure", projection.LastError);
    }

    [Fact]
    public void ProjectionAndTenantVersionsUseOptimisticConcurrency()
    {
        using var dbContext = CreateDbContext();

        Assert.True(dbContext.Model.FindEntityType(typeof(OverviewProjection))!
            .FindProperty(nameof(OverviewProjection.Generation))!.IsConcurrencyToken);
        Assert.True(dbContext.Model.FindEntityType(typeof(Tenant))!
            .FindProperty(nameof(Tenant.FinancialDataVersion))!.IsConcurrencyToken);
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FinyteDbContext(options);
    }

    private static Tenant CreateTenant(long financialDataVersion)
    {
        return new Tenant
        {
            ClerkOrganizationId = Guid.NewGuid().ToString("N"),
            Name = "Family",
            FinancialDataVersion = financialDataVersion,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static OverviewProjection CreateProjection(Guid tenantId, long sourceVersion)
    {
        var now = DateTimeOffset.UtcNow;
        var response = new OverviewResponse(
            new OverviewScopeResponse(null, "All accounts"),
            "2026-06",
            "AUD",
            100,
            20,
            1,
            new OverviewCashFlowRaceResponse(10, 20, -10),
            [],
            [],
            new OverviewFreshnessResponse(now, null, IsRefreshing: false));
        return new OverviewProjection
        {
            TenantId = tenantId,
            MonthKey = response.MonthKey,
            Currency = response.Currency,
            PayloadJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            SourceVersion = sourceVersion,
            Status = ProjectionStatus.Succeeded,
            CalculatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class RecordingOverviewProjector : IOverviewProjector
    {
        public int RebuildCount { get; private set; }

        public Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<OverviewResponse> Rebuild(OverviewProjectionScope scope, CancellationToken cancellationToken)
        {
            RebuildCount++;
            throw new NotSupportedException();
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
