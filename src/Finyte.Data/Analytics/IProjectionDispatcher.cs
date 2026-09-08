using Finyte.Core.Analytics;

namespace Finyte.Data.Analytics;

public interface IProjectionDispatcher
{
    Task<OverviewResponse> GetOrRebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken);

    Task<OverviewResponse> RebuildOverview(OverviewProjectionScope scope, CancellationToken cancellationToken);
}
