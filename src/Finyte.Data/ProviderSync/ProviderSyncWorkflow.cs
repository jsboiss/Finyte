using Temporalio.Common;
using Temporalio.Workflows;

namespace Finyte.Data.ProviderSync;

public sealed record ProviderSyncWorkflowInput(Guid BatchId, IReadOnlyList<Guid> SyncRunIds);

[Workflow]
public sealed class ProviderSyncWorkflow
{
    [WorkflowRun]
    public async Task Run(ProviderSyncWorkflowInput input)
    {
        foreach (var syncRunId in input.SyncRunIds)
        {
            await Workflow.ExecuteActivityAsync(
                (ProviderSyncActivities x) => x.Run(syncRunId),
                new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromMinutes(15),
                    HeartbeatTimeout = TimeSpan.FromMinutes(1),
                    RetryPolicy = new RetryPolicy
                    {
                        InitialInterval = TimeSpan.FromSeconds(2),
                        BackoffCoefficient = 2,
                        MaximumInterval = TimeSpan.FromMinutes(1),
                        MaximumAttempts = 5
                    }
                });
        }
    }
}
