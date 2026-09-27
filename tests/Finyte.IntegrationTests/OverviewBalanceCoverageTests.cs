using Finyte.Core.Accounts;
using Finyte.Core.Analytics;
using Finyte.Core.Tenancy;
using Finyte.Data;
using Finyte.Data.Analytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class OverviewBalanceCoverageTests
{
    [Fact]
    public async Task CoverageExcludesPlaceholdersAndIncludesReportedZeroBalances()
    {
        var options = new DbContextOptionsBuilder<FinyteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new FinyteDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant { ClerkOrganizationId = "balance-coverage", Name = "Household" };
        var reported = new Account { TenantId = tenant.Id, Name = "Reported", CurrentBalance = 500m, CreatedAt = now.AddDays(-1), BalanceAsOf = now };
        var providerZero = new Account { TenantId = tenant.Id, Name = "Provider zero", FiskilAccountId = "provider-zero", CreatedAt = now, BalanceAsOf = now };
        var importedZero = new Account { TenantId = tenant.Id, Name = "Imported zero", ManualBalanceVersion = 1, CreatedAt = now, BalanceAsOf = now };
        var legacy = new Account { TenantId = tenant.Id, Name = "Legacy", CustomName = "Legacy import", CurrentBalance = 900m, CreatedAt = now, BalanceAsOf = now };
        var missing = new Account { TenantId = tenant.Id, Name = "Missing", CurrentBalance = 200m, CreatedAt = now };
        dbContext.AddRange(tenant, reported, providerZero, importedZero, legacy, missing);
        await dbContext.SaveChangesAsync();
        var projector = new OverviewProjector(dbContext, TestCalendar.Tenants(dbContext));

        var result = await projector.Rebuild(new OverviewProjectionScope(tenant.Id, null, now.ToString("yyyy-MM")), CancellationToken.None);

        Assert.Equal(50000, result.AccountBalanceMinorUnits);
        Assert.NotNull(result.BalanceCoverage);
        Assert.Equal(3, result.BalanceCoverage.CoveredAccounts);
        Assert.Equal(5, result.BalanceCoverage.TotalAccounts);
        Assert.Equal(new[] { "Legacy import", "Missing" }, result.BalanceCoverage.MissingAccounts);

        var direct = await projector.Rebuild(new OverviewProjectionScope(tenant.Id, legacy.Id, now.ToString("yyyy-MM")), CancellationToken.None);

        Assert.Equal(0, direct.AccountBalanceMinorUnits);
        Assert.NotNull(direct.BalanceCoverage);
        Assert.Equal(0, direct.BalanceCoverage.CoveredAccounts);
        Assert.Equal(1, direct.BalanceCoverage.TotalAccounts);
        Assert.Equal(new[] { "Legacy import" }, direct.BalanceCoverage.MissingAccounts);
    }
}
