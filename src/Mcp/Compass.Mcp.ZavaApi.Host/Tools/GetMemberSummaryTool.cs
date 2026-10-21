using Compass.Mcp.ZavaApi.Application.Queries;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace Compass.Mcp.ZavaApi.Host.Tools;

/// <summary>MCP tool: a member's profile. Thin adapter — binds arguments and delegates to the query.</summary>
public sealed class GetMemberSummaryTool(ToolResultRunner runner)
{
    [Function(nameof(GetMemberSummaryTool))]
    public Task<string> RunAsync(
        [McpToolTrigger(ZavaApiToolNames.GetMemberSummary, "Gets a credit union member's profile: name, email, join date and status.")]
        ToolInvocationContext context,
        [McpToolProperty("memberNumber", "The member number, for example ZCU-100234.", true)]
        string memberNumber,
        CancellationToken cancellationToken) =>
        runner.RunAsync(new GetMemberSummaryQuery(memberNumber), cancellationToken);
}
