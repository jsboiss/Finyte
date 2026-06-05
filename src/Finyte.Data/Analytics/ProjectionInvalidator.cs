using Finyte.Core.Analytics;

namespace Finyte.Data.Analytics;

public sealed class ProjectionInvalidator(IProjectionDispatcher projectionDispatcher) : IProjectionInvalidator
{
    public async Task AccountBalanceChanged(Guid tenantId, Guid accountId, CancellationToken cancellationToken)
    {
        var monthKey = GetCurrentMonthKey();
        await RebuildOverviewAccountAndAll(tenantId, accountId, monthKey, cancellationToken);
    }

    public async Task TransactionChanged(Guid tenantId, Guid accountId, DateTimeOffset? postedAt, CancellationToken cancellationToken)
    {
        var monthKey = postedAt is null
            ? GetCurrentMonthKey()
            : ToMonthKey(postedAt.Value);
        await RebuildOverviewAccountAndAll(tenantId, accountId, monthKey, cancellationToken);
    }

    public async Task OverviewRequested(Guid tenantId, Guid? accountId, string monthKey, CancellationToken cancellationToken)
    {
        await projectionDispatcher.RebuildOverview(new OverviewProjectionScope(tenantId, accountId, monthKey), cancellationToken);
    }

    public async Task TenantProjectionDataChanged(Guid tenantId, string reason, CancellationToken cancellationToken)
    {
        await projectionDispatcher.MarkTenantStale(tenantId, reason, cancellationToken);
    }

    private async Task RebuildOverviewAccountAndAll(Guid tenantId, Guid accountId, string monthKey, CancellationToken cancellationToken)
    {
        await projectionDispatcher.RebuildOverview(new OverviewProjectionScope(tenantId, accountId, monthKey), cancellationToken);
        await projectionDispatcher.RebuildOverview(new OverviewProjectionScope(tenantId, AccountId: null, monthKey), cancellationToken);
    }

    private static string GetCurrentMonthKey()
    {
        return ToMonthKey(DateTimeOffset.UtcNow);
    }

    private static string ToMonthKey(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return $"{utc.Year:D4}-{utc.Month:D2}";
    }
}
