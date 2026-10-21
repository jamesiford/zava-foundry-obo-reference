using Compass.Mcp.ZavaApi.Application.Queries;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace Compass.Mcp.ZavaApi.Host.Tools;

/// <summary>MCP tool: recent transactions for one account. Thin adapter over the query.</summary>
public sealed class ListRecentTransactionsTool(ToolResultRunner runner)
{
    [Function(nameof(ListRecentTransactionsTool))]
    public Task<string> RunAsync(
        [McpToolTrigger(ZavaApiToolNames.ListRecentTransactions, "Lists the most recent transactions on an account, newest first.")]
        ToolInvocationContext context,
        [McpToolProperty("accountNumber", "The account number, for example 100234-D02.", true)]
        string accountNumber,
        [McpToolProperty("count", "How many transactions to return (1-100). Defaults to 10.", false)]
        int? count,
        CancellationToken cancellationToken) =>
        runner.RunAsync(
            new ListRecentTransactionsQuery(accountNumber, count ?? ListRecentTransactionsQuery.DefaultCount),
            cancellationToken);
}
