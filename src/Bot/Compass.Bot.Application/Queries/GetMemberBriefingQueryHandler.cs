using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Commands;
using Compass.Bot.Application.Models;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// Prepares a turn: resolve the person once and exchange once per downstream audience, then hand the fan-out to the
/// <see cref="IBriefingOrchestrator"/>.
/// </summary>
/// <remarks>
/// Identity stays here and the agent fan-out lives behind the orchestrator, so swapping the orchestration engine can
/// never change who the turn runs as. Fails closed: any auth failure from the exchange or an agent aborts the whole
/// turn. There is no partial answer and no fallback to an app-only identity, because a half-answer built from a
/// different identity is exactly the leak this design exists to prevent (contract rule 5).
/// </remarks>
public sealed class GetMemberBriefingQueryHandler(
    ISender sender,
    IPersonContextResolver personResolver,
    IMemberNumberResolver memberResolver,
    IBriefingOrchestrator orchestrator,
    BriefingRoutePolicy routePolicy)
    : IRequestHandler<GetMemberBriefingQuery, MemberBriefing>
{
    public async Task<MemberBriefing> HandleAsync(GetMemberBriefingQuery request, CancellationToken cancellationToken)
    {
        var person = await personResolver.ResolveAsync(request.SsoToken, cancellationToken);
        var session = SessionId.Mint(person);

        // Two gates that can only remove work, both before any token is exchanged: the deployment's route (configuration)
        // and what this question is about (per turn). A branch out of scope is never built, so its token is never needed.
        var scope = routePolicy.ScopeFor(QuestionFocusClassifier.Classify(request.Question));

        // The member record is only required when the accounts branch runs, so a policy question needs no linked member.
        var memberNumber = scope.Accounts ? memberResolver.Resolve(person) : null;

        // Only the audiences in scope: a token that is never acquired can never be misused.
        var tokens = new DownstreamTokenSet();
        foreach (var audience in scope.Audiences)
        {
            var token = await sender.SendAsync(new AcquireDownstreamTokenCommand(request.SsoToken, audience), cancellationToken);
            tokens.Add(audience, token);
        }
        return await orchestrator.RunAsync(new BriefingPlan(person, session, tokens, request.Question, memberNumber, scope), cancellationToken, request.Progress);
    }
}
