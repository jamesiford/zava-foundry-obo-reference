using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Hosted.SharePointRetrieval;

/// <summary>
/// Retrieves SharePoint content through the Microsoft 365 Copilot Retrieval API using the <em>person's</em> Graph
/// token, so SharePoint itself decides what that person may see (brief §5.1).
/// </summary>
/// <remarks>
/// Contract rule 4 — "the store decides": this class must never post-filter, rank-prune or otherwise second-guess
/// the hits. Contract rule 5 — fails closed: with no forwarded Graph token it throws rather than retrying with an
/// app-only identity, which would see every document and defeat the demo.
/// </remarks>
public sealed class SharePointRetrievalAgent(IGraphRetrievalClient graph) : IHostedAgent<RetrievalRequest, RetrievalResult>
{
    public async Task<RetrievalResult> HandleAsync(RetrievalRequest request, AgentCallContext context, CancellationToken ct)
    {
        var graphToken = context.RequireToken(CompassHeaders.GraphAccessToken);

        // The token is handed on exactly as received and used for this single call.
        return await graph.RetrieveAsync(graphToken, request.Query, request.MaxResults, ct);
    }
}
