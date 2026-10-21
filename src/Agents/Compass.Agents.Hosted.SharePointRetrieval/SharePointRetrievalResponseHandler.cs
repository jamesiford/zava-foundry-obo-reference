using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosting;
using Microsoft.Extensions.Logging;

namespace Compass.Agents.Hosted.SharePointRetrieval;

/// <summary>
/// Binds the SharePoint retrieval agent to the Foundry Responses adapter. It holds no logic of its own so the agent stays
/// testable without the hosting package (dev spec §6, one handler per use case).
/// </summary>
public sealed class SharePointRetrievalResponseHandler(
    IHostedAgent<RetrievalRequest, RetrievalResult> agent,
    ILogger<SharePointRetrievalResponseHandler> logger)
    : HostedAgentResponseHandler<RetrievalRequest, RetrievalResult>(agent, logger);
