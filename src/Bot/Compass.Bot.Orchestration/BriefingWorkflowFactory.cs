using Compass.Bot.Application.Models;
using Compass.Bot.Orchestration.Executors;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration;

/// <summary>
/// Declares the briefing graph: start → the Zava API branch plus the SharePoint branch(es) the route selects → one
/// compose node. Kept apart from the code that runs it so the shape of the turn is readable in one place.
/// </summary>
/// <remarks>
/// The turn's scope (the route narrowed by the question) is applied here, at composition time: a branch outside it is never
/// built, so it cannot run and its downstream token (which the handler doesn't acquire either) is never needed.
/// </remarks>
internal static class BriefingWorkflowFactory
{
    public static Workflow Create(ISender sender, BriefingPlan plan, BriefingRoutePolicy route)
    {
        var start = new BriefingStartExecutor();
        var compose = new BriefingComposeExecutor(plan.Person, plan.Scope, route.Route);

        var branches = new List<ExecutorBinding>();
        if (plan.Scope.Retrieval)
        {
            branches.Add(new SharePointRetrievalExecutor(sender));
        }

        if (plan.Scope.Knowledge)
        {
            branches.Add(new SharePointKnowledgeExecutor(sender));
        }

        if (plan.Scope.Accounts)
        {
            branches.Add(new ZavaApiExecutor(sender));
        }

        return new WorkflowBuilder(start)
            .WithName("compass-member-briefing")
            // Opt-in: without this the workflow emits no spans. Content (executor input and output, message bodies) is recorded
            // only when EnableSensitiveData is set, and it is not, because the messages carry member data.
            .WithOpenTelemetry()
            .AddFanOutEdge(start, branches)
            .AddFanInBarrierEdge(branches, compose)
            .WithOutputFrom(compose)
            .Build();
    }
}
