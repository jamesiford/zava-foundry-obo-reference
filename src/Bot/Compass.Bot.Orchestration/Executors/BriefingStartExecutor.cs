using Compass.Bot.Application.Models;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>
/// The workflow's entry node. It only forwards the plan to the three branches; it holds no identity of its own, so the
/// person's tokens are visible only to the branch that needs them.
/// </summary>
internal sealed class BriefingStartExecutor() : Executor<BriefingPlan, BriefingPlan>(BriefingExecutorIds.Start)
{
    public override ValueTask<BriefingPlan> HandleAsync(BriefingPlan message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(message);
}
