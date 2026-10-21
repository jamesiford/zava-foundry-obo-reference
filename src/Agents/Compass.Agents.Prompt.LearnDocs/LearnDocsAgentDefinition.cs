using Azure.AI.Projects.Agents;
using OpenAI.Responses;

namespace Compass.Agents.Prompt.LearnDocs;

/// <summary>
/// The whole agent: a model, instructions and one tool. No project connection, headers, structured inputs or secrets,
/// because the Microsoft Learn MCP server is public.
/// </summary>
public static class LearnDocsAgentDefinition
{
    public const string Instructions = """
        Answer questions about Microsoft Foundry, Microsoft Agent Framework, MCP and Azure using only the Microsoft Learn tool.
        Cite the Learn page URLs you used. If the tool returns nothing relevant, say you could not find it.
        """;

    public static DeclarativeAgentDefinition Build(LearnDocsAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Foundry calls the server for the agent. No approval prompt: it is read-only documentation search.
        McpTool learn = ResponseTool.CreateMcpTool(
            serverLabel: options.ServerLabel,
            serverUri: options.McpServerUri,
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));

        return new DeclarativeAgentDefinition(options.ModelDeployment)
        {
            Instructions = Instructions,
            Tools = { learn },
        };
    }
}
