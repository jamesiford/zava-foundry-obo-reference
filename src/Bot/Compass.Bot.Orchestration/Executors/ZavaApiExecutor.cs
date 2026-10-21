using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>Branch 3: the member's accounts through the Zava API token.</summary>
internal sealed class ZavaApiExecutor(ISender sender) : Executor<BriefingPlan, BranchResult>(BriefingExecutorIds.ZavaApi)
{
    public override async ValueTask<BranchResult> HandleAsync(BriefingPlan message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        BranchResult.FromAccounts(await sender.SendAsync(
            new InvokeZavaApiAgentQuery(message.Person, message.Session, message.Tokens.Get(DownstreamAudience.ZavaApi), message.MemberNumber ?? throw new InvalidOperationException("The accounts branch ran without a resolved member number.")),
            cancellationToken));
}
