using Azure.AI.Projects.Agents;
using OpenAI.Responses;

namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// The SharePoint knowledge agent as code: a Foundry <em>prompt</em> agent whose only tool is the Foundry IQ knowledge
/// base (an MCP server). Kept as a pure builder so the definition is reviewable and unit-testable without a Foundry
/// project (dev spec §6: one agent, one definition).
/// </summary>
/// <remarks>
/// The security-relevant detail is the header binding. The knowledge base's remote SharePoint source runs the query
/// <em>as the person</em> when it receives their token in <c>x-ms-query-source-authorization</c>. The token is not
/// baked into the definition (versions are immutable and shared by everyone); the definition holds a
/// <c>{{search_auth_token}}</c> placeholder bound to a required structured input that the caller supplies per request.
/// </remarks>
public static class SharePointKnowledgeAgentDefinition
{
    /// <summary>Header the knowledge base reads to run permission-trimmed retrieval as the person.</summary>
    public const string QuerySourceAuthorizationHeader = "x-ms-query-source-authorization";

    /// <summary>Name of the structured input that carries the person's Search token on each invocation.</summary>
    public const string SearchTokenInputName = "search_auth_token";

    public const string ToolServerLabel = "knowledge-base";

    /// <summary>The only tool Foundry supports for a knowledge base MCP endpoint.</summary>
    public const string RetrieveToolName = "knowledge_base_retrieve";

    public const string Instructions = """
        You answer questions about Zava Credit Union using only the knowledge base tool. Never answer from your own knowledge.
        Every answer must cite the sources the tool returned, rendered as: 【message_idx:search_idx†source_name】
        If the knowledge base returns nothing relevant, answer exactly "I don't know" — that means the person has no
        access to a relevant document, not that none exists.
        """;

    public static DeclarativeAgentDefinition Build(KnowledgeAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var mcpEndpoint = options.KnowledgeBaseMcpEndpoint
            ?? throw new InvalidOperationException($"{KnowledgeAgentOptions.SectionName}:KnowledgeBaseMcpEndpoint is not configured.");

        McpTool tool = ResponseTool.CreateMcpTool(
            serverLabel: ToolServerLabel,
            serverUri: mcpEndpoint,
            allowedTools: new McpToolFilter { ToolNames = { RetrieveToolName } },
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));
        tool.ProjectConnectionId = options.ProjectConnectionName;
        tool.Headers[QuerySourceAuthorizationHeader] = $"{{{{{SearchTokenInputName}}}}}";

        var definition = new DeclarativeAgentDefinition(options.ModelDeployment)
        {
            Instructions = Instructions,
            Tools = { tool },
        };
        definition.StructuredInputs[SearchTokenInputName] = new StructuredInputDefinition
        {
            IsRequired = true,
            Description = "The person's Azure AI Search bearer token (on-behalf-of), forwarded unchanged.",
            Schema = { ["type"] = BinaryData.FromString("\"string\"") },
        };
        return definition;
    }
}
