namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Settings for the SharePoint answer prompt agent: the tool-less agent that writes the Graph retrieval answer (route A)
/// from extracts the bot already fetched as the person. It mirrors <see cref="KnowledgeAgentOptions"/> and shares the same
/// project and model deployment.
/// </summary>
public sealed class AnswerAgentOptions
{
    public const string SectionName = "Bot:Foundry:AnswerAgent";

    /// <summary>Name of the Foundry prompt agent. Versions are immutable, so republishing creates a new version under this name.</summary>
    public string AgentName { get; set; } = "SharePointAnswerAgent";

    /// <summary>Pinned version to invoke; <c>null</c> invokes the latest, which is what a freshly published definition needs.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>The Foundry model deployment the prompt agent runs on (the project's existing one).</summary>
    public string ModelDeployment { get; set; } = "gpt-5.6-sol";
}
