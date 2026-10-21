using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

public sealed class InvokeSharePointRetrievalAgentQueryHandler(IHostedAgentGateway gateway)
    : IRequestHandler<InvokeSharePointRetrievalAgentQuery, RetrievalResult>
{
    public Task<RetrievalResult> HandleAsync(InvokeSharePointRetrievalAgentQuery request, CancellationToken cancellationToken)
    {
        var call = new HostedAgentCall(
            HostedAgentName.SharePointRetrieval,
            request.Person,
            request.Session,
            new Dictionary<string, AccessToken> { [CompassHeaders.GraphAccessToken] = request.GraphToken });

        return gateway.InvokeAsync<RetrievalRequest, RetrievalResult>(call, new RetrievalRequest(request.Query), cancellationToken);
    }
}
