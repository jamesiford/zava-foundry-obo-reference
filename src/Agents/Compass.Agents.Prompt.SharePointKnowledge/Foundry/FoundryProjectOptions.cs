namespace Compass.Agents.Prompt.SharePointKnowledge.Foundry;

/// <summary>Where the Foundry project lives; shared by every client that talks to the Agent Service.</summary>
public sealed class FoundryProjectOptions
{
    public const string SectionName = "Bot:Foundry";

    /// <summary>The project endpoint, e.g. <c>https://{account}.services.ai.azure.com/api/projects/{project}</c>.</summary>
    public Uri? ProjectEndpoint { get; set; }
}
