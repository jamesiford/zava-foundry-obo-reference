using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>Branch 1: SharePoint through the Graph-scope token (the Graph retrieval route).</summary>
/// <remarks>Goes through the same <see cref="ISender"/> query as before, so the per-agent handler and its logging are unchanged.</remarks>
internal sealed class SharePointRetrievalExecutor(ISender sender) : Executor<BriefingPlan, BranchResult>(BriefingExecutorIds.Retrieval)
{
    public override async ValueTask<BranchResult> HandleAsync(BriefingPlan message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        BranchResult.FromDocuments(await sender.SendAsync(
            new InvokeSharePointRetrievalAgentQuery(message.Person, message.Session, message.Tokens.Get(DownstreamAudience.Graph), message.Question),
            cancellationToken));
}
