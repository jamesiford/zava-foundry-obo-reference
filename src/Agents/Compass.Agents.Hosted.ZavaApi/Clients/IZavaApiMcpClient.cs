using Compass.Agents.Contracts.Briefing;

namespace Compass.Agents.Hosted.ZavaApi.Clients;

/// <summary>
/// Narrow seam over the Zava Credit Union MCP server (dev spec §6, Interface Segregation). The person's API token is
/// a per-call argument so no implementation can keep it beyond one call.
/// </summary>
public interface IZavaApiMcpClient
{
    Task<MemberBriefingResult> GetMemberBriefingAsync(string apiToken, string memberNumber, CancellationToken ct);
}
