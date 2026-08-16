namespace Finyte.Data.Temporal;

public interface ITemporalDashboardDispatcher
{
    Task<string> Dispatch(Guid projectionId, long generation, CancellationToken cancellationToken);
}
