using Finyte.Core.Analytics;

namespace Finyte.Data.Analytics;

public interface IOverviewProjector
{
    Task<OverviewResponse> GetOrRebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken);

    Task<OverviewResponse> Rebuild(Guid tenantId, Guid? accountId, CancellationToken cancellationToken);
}
