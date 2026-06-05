namespace Finyte.Data.Analytics;

public interface IProjectionInvalidator
{
    Task AccountBalanceChanged(Guid tenantId, Guid accountId, CancellationToken cancellationToken);

    Task TransactionChanged(Guid tenantId, Guid accountId, DateTimeOffset? postedAt, CancellationToken cancellationToken);

    Task OverviewRequested(Guid tenantId, Guid? accountId, string monthKey, CancellationToken cancellationToken);

    Task TenantProjectionDataChanged(Guid tenantId, string reason, CancellationToken cancellationToken);
}
