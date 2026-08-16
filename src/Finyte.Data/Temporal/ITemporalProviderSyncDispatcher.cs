namespace Finyte.Data.Temporal;

public interface ITemporalProviderSyncDispatcher
{
    Task<string> Dispatch(Guid batchId, IReadOnlyList<Guid> syncRunIds, CancellationToken cancellationToken);
}
