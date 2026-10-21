using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Hosting;
using Microsoft.Extensions.Logging;

namespace Compass.Agents.Hosted.ZavaApi;

/// <summary>
/// Binds the Zava API agent to the Foundry Responses adapter. It holds no logic of its own so the agent stays testable
/// without the hosting package (dev spec §6, one handler per use case).
/// </summary>
public sealed class ZavaApiResponseHandler(
    IHostedAgent<MemberBriefingRequest, MemberBriefingResult> agent,
    ILogger<ZavaApiResponseHandler> logger)
    : HostedAgentResponseHandler<MemberBriefingRequest, MemberBriefingResult>(agent, logger);
