using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>Branch 2: SharePoint through the Search-scope token (the Foundry IQ knowledge-base route).</summary>
internal sealed class SharePointKnowledgeExecutor(ISender sender) : Executor<BriefingPlan, BranchResult>(BriefingExecutorIds.Knowledge)
{
    public override async ValueTask<BranchResult> HandleAsync(BriefingPlan message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        BranchResult.FromKnowledge(await sender.SendAsync(
            new InvokeSharePointKnowledgeAgentQuery(message.Person, message.Session, message.Tokens.Get(DownstreamAudience.Search), message.Question),
            cancellationToken));
}
