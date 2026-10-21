namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Settings for the SharePoint knowledge prompt agent. The knowledge base itself (and the remote SharePoint source
/// behind it) is data-plane configuration provisioned by <c>infra/scripts/Setup-KnowledgeBase.ps1</c>; this only
/// records the names the agent definition has to point at.
/// </summary>
public sealed class KnowledgeAgentOptions
{
    public const string SectionName = "Bot:Foundry:KnowledgeAgent";

    /// <summary>Name of the Foundry prompt agent. Versions are immutable, so republishing creates a new version under this name.</summary>
    public string AgentName { get; set; } = "SharePointKnowledgeAgent";

    /// <summary>Pinned version to invoke; <c>null</c> invokes the latest, which is what a freshly published definition needs.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>The Foundry model deployment the prompt agent runs on.</summary>
    public string ModelDeployment { get; set; } = "gpt-5.6-sol";

    /// <summary>The knowledge base's MCP endpoint: <c>{search}/knowledgebases/{kb}/mcp?api-version=…</c>.</summary>
    public Uri? KnowledgeBaseMcpEndpoint { get; set; }

    /// <summary>Name of the Foundry project connection (category RemoteTool) that points at <see cref="KnowledgeBaseMcpEndpoint"/>.</summary>
    public string ProjectConnectionName { get; set; } = "compass-knowledge-base";
}
