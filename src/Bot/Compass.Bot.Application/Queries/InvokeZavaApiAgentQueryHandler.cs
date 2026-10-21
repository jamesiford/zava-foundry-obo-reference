using Compass.Agents.Contracts.Briefing;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

public sealed class InvokeZavaApiAgentQueryHandler(IHostedAgentGateway gateway)
    : IRequestHandler<InvokeZavaApiAgentQuery, MemberBriefingResult>
{
    public Task<MemberBriefingResult> HandleAsync(InvokeZavaApiAgentQuery request, CancellationToken cancellationToken)
    {
        var call = new HostedAgentCall(
            HostedAgentName.ZavaApi,
            request.Person,
            request.Session,
            new Dictionary<string, AccessToken> { [CompassHeaders.ZavaApiToken] = request.ZavaApiToken });

        return gateway.InvokeAsync<MemberBriefingRequest, MemberBriefingResult>(call, new MemberBriefingRequest(request.MemberNumber), cancellationToken);
    }
}
