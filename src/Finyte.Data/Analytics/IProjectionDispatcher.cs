using Finyte.Core.Analytics;

namespace Finyte.Data.Analytics;

public interface IProjectionDispatcher
{
    Task<T> Run<T>(ProjectionRequest request, Func<CancellationToken, Task<T>> rebuild, CancellationToken cancellationToken);

    Task<OverviewResponse> GetOrRebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken);

    Task<OverviewResponse> RebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken);

    Task MarkTenantStale(Guid tenantId, string reason, CancellationToken cancellationToken);
}
