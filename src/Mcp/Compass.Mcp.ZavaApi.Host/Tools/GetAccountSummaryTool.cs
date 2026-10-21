using Compass.Mcp.ZavaApi.Application.Queries;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace Compass.Mcp.ZavaApi.Host.Tools;

/// <summary>MCP tool: a member's accounts and balances. Thin adapter over the query.</summary>
public sealed class GetAccountSummaryTool(ToolResultRunner runner)
{
    [Function(nameof(GetAccountSummaryTool))]
    public Task<string> RunAsync(
        [McpToolTrigger(ZavaApiToolNames.GetAccountSummary, "Gets all of a member's accounts with balances and the net total.")]
        ToolInvocationContext context,
        [McpToolProperty("memberNumber", "The member number, for example ZCU-100234.", true)]
        string memberNumber,
        CancellationToken cancellationToken) =>
        runner.RunAsync(new GetAccountSummaryQuery(memberNumber), cancellationToken);
}
