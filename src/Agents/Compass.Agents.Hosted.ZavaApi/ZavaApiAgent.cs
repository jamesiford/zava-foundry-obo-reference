using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Hosted.ZavaApi.Clients;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Hosted.ZavaApi;

/// <summary>
/// Fetches a member's account picture from the Zava Credit Union MCP server using the person's own API token.
/// </summary>
/// <remarks>
/// Fails closed (contract rule 5) when the token is missing, and forwards it unchanged — never exchanged or
/// re-minted — so the MCP server sees, and validates, exactly the identity the bot obtained for this person.
/// </remarks>
public sealed class ZavaApiAgent(IZavaApiMcpClient mcp) : IHostedAgent<MemberBriefingRequest, MemberBriefingResult>
{
    public async Task<MemberBriefingResult> HandleAsync(MemberBriefingRequest request, AgentCallContext context, CancellationToken ct)
    {
        var apiToken = context.RequireToken(CompassHeaders.ZavaApiToken);

        return await mcp.GetMemberBriefingAsync(apiToken, request.MemberNumber, ct);
    }
}
