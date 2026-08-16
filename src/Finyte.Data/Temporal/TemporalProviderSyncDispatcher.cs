using Finyte.Data.ProviderSync;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Finyte.Data.Temporal;

public sealed class TemporalProviderSyncDispatcher : ITemporalProviderSyncDispatcher
{
    public TemporalProviderSyncDispatcher(IOptions<TemporalOptions> options)
    {
        Options = options.Value;
        Client = new Lazy<Task<TemporalClient>>(() => TemporalClient.ConnectAsync(new TemporalClientConnectOptions(Options.Address)
        {
            Namespace = Options.Namespace
        }));
    }

    private TemporalOptions Options { get; }

    private Lazy<Task<TemporalClient>> Client { get; }

    public async Task<string> Dispatch(Guid batchId, IReadOnlyList<Guid> syncRunIds, CancellationToken cancellationToken)
    {
        var workflowId = $"provider-sync-{batchId:N}";
        var client = await Client.Value;

        try
        {
            await client.StartWorkflowAsync(
                (ProviderSyncWorkflow x) => x.Run(new ProviderSyncWorkflowInput(batchId, syncRunIds)),
                new WorkflowOptions(workflowId, Options.TaskQueue));
        }
        catch (WorkflowAlreadyStartedException)
        {
        }

        return workflowId;
    }
}
