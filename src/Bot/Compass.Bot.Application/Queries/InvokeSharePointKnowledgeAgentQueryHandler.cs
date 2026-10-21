using Compass.Agents.Contracts.Knowledge;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

public sealed class InvokeSharePointKnowledgeAgentQueryHandler(IHostedAgentGateway gateway)
    : IRequestHandler<InvokeSharePointKnowledgeAgentQuery, KnowledgeQueryResult>
{
    public Task<KnowledgeQueryResult> HandleAsync(InvokeSharePointKnowledgeAgentQuery request, CancellationToken cancellationToken)
    {
        var call = new HostedAgentCall(
            HostedAgentName.SharePointKnowledge,
            request.Person,
            request.Session,
            new Dictionary<string, AccessToken> { [CompassHeaders.SearchToken] = request.SearchToken });

        return gateway.InvokeAsync<KnowledgeQueryRequest, KnowledgeQueryResult>(call, new KnowledgeQueryRequest(request.Question), cancellationToken);
    }
}
