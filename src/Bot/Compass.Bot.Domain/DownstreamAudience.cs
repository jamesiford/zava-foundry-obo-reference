namespace Compass.Bot.Domain;

/// <summary>The downstream APIs the bot exchanges the person's token for — one OBO exchange per audience per turn (contract rule 2).</summary>
public enum DownstreamAudience
{
    /// <summary>Microsoft Graph, used by the SharePoint retrieval agent.</summary>
    Graph,

    /// <summary>The Zava Credit Union API, used by the Zava API agent via the MCP server.</summary>
    ZavaApi,

    /// <summary>Azure AI Search, used by the SharePoint knowledge agent's Foundry IQ knowledge base (remote SharePoint source).</summary>
    Search,
}
