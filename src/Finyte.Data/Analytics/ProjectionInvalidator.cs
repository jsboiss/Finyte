using Finyte.Core.Analytics;
using Finyte.Core.Scheduling;
using Finyte.Data.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Finyte.Data.Analytics;

public sealed class ProjectionInvalidator(FinyteDbContext dbContext, TenantCalendars calendars) : IProjectionInvalidator
{
    public async Task AccountBalanceChanged(Guid tenantId, Guid accountId, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        await MarkChanged(tenantId, accountId, calendar.CurrentMonthKey, includeAllAccounts: true, cancellationToken);
    }

    public async Task TransactionChanged(Guid tenantId, Guid accountId, DateTimeOffset? postedAt, CancellationToken cancellationToken)
    {
        var calendar = await calendars.For(tenantId, cancellationToken);
        var monthKey = postedAt is null ? calendar.CurrentMonthKey : FinancialCalendar.MonthKey(calendar.ToDate(postedAt.Value));
        await MarkChanged(tenantId, accountId, monthKey, includeAllAccounts: true, cancellationToken);
    }

    public async Task OverviewRequested(Guid tenantId, Guid? accountId, string monthKey, CancellationToken cancellationToken)
    {
        var projections = await dbContext.OverviewProjections
            .Where(x => x.TenantId == tenantId && x.AccountId == accountId && x.MonthKey == monthKey)
            .ToListAsync(cancellationToken);
        MarkPending(projections);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task TenantProjectionDataChanged(Guid tenantId, string reason, CancellationToken cancellationToken)
    {
        await AdvanceVersion(tenantId, cancellationToken);
        var projections = await dbContext.OverviewProjections
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        MarkPending(projections);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkChanged(
        Guid tenantId,
        Guid? accountId,
        string monthKey,
        bool includeAllAccounts,
        CancellationToken cancellationToken)
    {
        await AdvanceVersion(tenantId, cancellationToken);
        var projections = await dbContext.OverviewProjections
            .Where(x => x.TenantId == tenantId
                && x.MonthKey == monthKey
                && (x.AccountId == accountId || (includeAllAccounts && x.AccountId == null)))
            .ToListAsync(cancellationToken);
        MarkPending(projections);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AdvanceVersion(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants.SingleAsync(x => x.Id == tenantId, cancellationToken);
        tenant.FinancialDataVersion++;
    }

    private static void MarkPending(IEnumerable<OverviewProjection> projections)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var projection in projections)
        {
            projection.Generation++;
            projection.Status = ProjectionStatus.Pending;
            projection.LastError = null;
            projection.TemporalWorkflowId = null;
            projection.DispatchedAt = null;
            projection.InvalidatedAt = now;
            projection.UpdatedAt = now;
        }
    }
}
