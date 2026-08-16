using Finyte.Core.Analytics;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ProjectionInvalidatorTests
{
    [Fact]
    public async Task TransactionChangedAdvancesVersionAndQueuesAccountAndCombinedSnapshots()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant();
        var accountId = Guid.NewGuid();
        var accountProjection = CreateProjection(tenant.Id, accountId, "2026-06");
        var allProjection = CreateProjection(tenant.Id, null, "2026-06");
        var otherMonth = CreateProjection(tenant.Id, accountId, "2026-05");
        dbContext.AddRange(tenant, accountProjection, allProjection, otherMonth);
        await dbContext.SaveChangesAsync();
        var invalidator = new ProjectionInvalidator(dbContext);

        await invalidator.TransactionChanged(
            tenant.Id,
            accountId,
            new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(1, tenant.FinancialDataVersion);
        Assert.Equal(ProjectionStatus.Pending, accountProjection.Status);
        Assert.Equal(ProjectionStatus.Pending, allProjection.Status);
        Assert.Equal(ProjectionStatus.Succeeded, otherMonth.Status);
    }

    [Fact]
    public async Task TenantChangeQueuesEveryExistingSnapshot()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant();
        var projections = new[]
        {
            CreateProjection(tenant.Id, null, "2026-05"),
            CreateProjection(tenant.Id, Guid.NewGuid(), "2026-06")
        };
        dbContext.Add(tenant);
        dbContext.AddRange(projections);
        await dbContext.SaveChangesAsync();
        var invalidator = new ProjectionInvalidator(dbContext);

        await invalidator.TenantProjectionDataChanged(tenant.Id, "broad change", CancellationToken.None);

        Assert.Equal(1, tenant.FinancialDataVersion);
        Assert.All(projections, x => Assert.Equal(ProjectionStatus.Pending, x.Status));
        Assert.All(projections, x => Assert.NotNull(x.InvalidatedAt));
    }

    [Fact]
    public async Task ManualRefreshQueuesOnlyRequestedSnapshotWithoutChangingDataVersion()
    {
        await using var dbContext = CreateDbContext();
        var tenant = CreateTenant();
        var requested = CreateProjection(tenant.Id, null, "2026-06");
        var other = CreateProjection(tenant.Id, Guid.NewGuid(), "2026-06");
        dbContext.AddRange(tenant, requested, other);
        await dbContext.SaveChangesAsync();
        var invalidator = new ProjectionInvalidator(dbContext);

        await invalidator.OverviewRequested(tenant.Id, null, "2026-06", CancellationToken.None);

        Assert.Equal(0, tenant.FinancialDataVersion);
        Assert.Equal(1, requested.Generation);
        Assert.Equal(ProjectionStatus.Pending, requested.Status);
        Assert.Equal(0, other.Generation);
        Assert.Equal(ProjectionStatus.Succeeded, other.Status);
    }

    private static FinyteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FinyteDbContext(options);
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            ClerkOrganizationId = Guid.NewGuid().ToString("N"),
            Name = "Family",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static OverviewProjection CreateProjection(Guid tenantId, Guid? accountId, string monthKey)
    {
        var now = DateTimeOffset.UtcNow;
        return new OverviewProjection
        {
            TenantId = tenantId,
            AccountId = accountId,
            MonthKey = monthKey,
            PayloadJson = "{}",
            Status = ProjectionStatus.Succeeded,
            CalculatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
