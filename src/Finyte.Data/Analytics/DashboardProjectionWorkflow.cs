using Temporalio.Common;
using Temporalio.Workflows;

namespace Finyte.Data.Analytics;

[Workflow]
public sealed class DashboardProjectionWorkflow
{
    [WorkflowRun]
    public async Task Run(Guid projectionId)
    {
        await Workflow.ExecuteActivityAsync(
            (DashboardProjectionActivities x) => x.Rebuild(projectionId),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(10),
                HeartbeatTimeout = TimeSpan.FromMinutes(1),
                RetryPolicy = new RetryPolicy
                {
                    MaximumAttempts = 5,
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2
                }
            });
    }
}
