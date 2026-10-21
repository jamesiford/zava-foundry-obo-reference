namespace Compass.Agents.Prompt.LearnDocs;

/// <summary>Settings for the Learn docs prompt agent. The MCP server is public, so there is nothing secret here.</summary>
public sealed class LearnDocsAgentOptions
{
    /// <summary>Name of the Foundry prompt agent. Versions are immutable, so republishing creates a new version under this name.</summary>
    public string AgentName { get; set; } = "LearnDocsAgent";

    /// <summary>The Foundry model deployment the prompt agent runs on.</summary>
    public string ModelDeployment { get; set; } = "gpt-5.6-sol";

    public Uri McpServerUri { get; set; } = new("https://learn.microsoft.com/api/mcp");

    public string ServerLabel { get; set; } = "microsoft-learn";
}
