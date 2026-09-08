using Finyte.Data.Analytics;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Finyte.Data.Temporal;

public sealed class TemporalDashboardDispatcher : ITemporalDashboardDispatcher
{
    public TemporalDashboardDispatcher(IOptions<TemporalOptions> options)
    {
        Options = options.Value;
        Client = new Lazy<Task<TemporalClient>>(() => TemporalClient.ConnectAsync(new TemporalClientConnectOptions(Options.Address)
        {
            Namespace = Options.Namespace
        }));
    }

    private TemporalOptions Options { get; }

    private Lazy<Task<TemporalClient>> Client { get; }

    public async Task<string> Dispatch(Guid projectionId, long generation, CancellationToken cancellationToken)
    {
        var workflowId = $"dashboard-{projectionId:N}-g{generation}";
        var client = await Client.Value;

        try
        {
            await client.StartWorkflowAsync(
                (DashboardProjectionWorkflow x) => x.Run(projectionId),
                new WorkflowOptions(workflowId, Options.TaskQueue));
        }
        catch (WorkflowAlreadyStartedException)
        {
        }

        return workflowId;
    }
}
