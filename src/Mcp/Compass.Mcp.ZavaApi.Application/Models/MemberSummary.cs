using Compass.Mcp.ZavaApi.Domain;

namespace Compass.Mcp.ZavaApi.Application.Models;

/// <summary>Member profile as exposed to MCP callers. A projection, so domain changes don't silently widen the tool's output.</summary>
public sealed record MemberSummary(
    string MemberNumber,
    string FullName,
    string Email,
    DateOnly JoinedOn,
    string Status)
{
    public static MemberSummary From(Member member) => new(
        member.MemberNumber,
        member.FullName,
        member.Email,
        member.JoinedOn,
        member.Status.ToString());
}
