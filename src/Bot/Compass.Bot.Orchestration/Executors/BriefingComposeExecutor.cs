using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Models;
using Compass.Bot.Domain;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>
/// The fan-in node: waits for every branch the turn's scope runs, then composes the single answer. It emits nothing until
/// every one has reported, so a failed branch can never yield a half-built briefing.
/// </summary>
/// <remarks>One instance per run (the workflow is built per turn), so the collected results can't cross people.</remarks>
internal sealed class BriefingComposeExecutor(PersonContext person, BriefingScope scope, SharePointRoute route) : Executor<BranchResult>(BriefingExecutorIds.Compose)
{
    private RetrievalResult? _documents;
    private KnowledgeQueryResult? _knowledge;
    private MemberBriefingResult? _accounts;

    /// <summary>The workflow rejects outputs whose type wasn't declared up front, so the briefing is declared here.</summary>
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        base.ConfigureProtocol(protocolBuilder).YieldsOutput<MemberBriefing>();

    public override async ValueTask HandleAsync(BranchResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _documents ??= message.Documents;
        _knowledge ??= message.Knowledge;
        _accounts ??= message.Accounts;

        if ((scope.Retrieval && _documents is null) || (scope.Knowledge && _knowledge is null) || (scope.Accounts && _accounts is null))
        {
            return;
        }

        var briefing = new MemberBriefing(
            person,
            BriefingComposer.Compose(_accounts, _documents, _knowledge, route),
            _accounts,
            _documents,
            _knowledge,
            route);
        await context.YieldOutputAsync(briefing, cancellationToken);
    }
}
