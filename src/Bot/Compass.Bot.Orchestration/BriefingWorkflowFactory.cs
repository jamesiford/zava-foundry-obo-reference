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
        var results = new List<ExecutorBinding>();
        var edges = new List<(ExecutorBinding From, ExecutorBinding To)>();
        if (plan.Scope.Retrieval)
        {
            // Retrieval is two steps (fetch, then gate and write the answer); the second is what the compose node waits for.
            ExecutorBinding fetch = new SharePointRetrievalExecutor(sender);
            ExecutorBinding answer = new RetrievalAnswerExecutor(sender, plan.Question);
            branches.Add(fetch);
            results.Add(answer);
            edges.Add((fetch, answer));
        }

        if (plan.Scope.Knowledge)
        {
            ExecutorBinding knowledge = new SharePointKnowledgeExecutor(sender);
            branches.Add(knowledge);
            results.Add(knowledge);
        }

        if (plan.Scope.Accounts)
        {
            ExecutorBinding accounts = new ZavaApiExecutor(sender);
            branches.Add(accounts);
            results.Add(accounts);
        }

        var builder = new WorkflowBuilder(start)
            .WithName("compass-member-briefing")
            // Opt-in: without this the workflow emits no spans. Content (executor input and output, message bodies) is recorded
            // only when EnableSensitiveData is set, and it is not, because the messages carry member data.
            .WithOpenTelemetry()
            .AddFanOutEdge(start, branches);
        foreach (var (from, to) in edges)
        {
            builder = builder.AddEdge(from, to);
        }

        return builder
            .AddFanInBarrierEdge(results, compose)
            .WithOutputFrom(compose)
            .Build();
    }
}
