namespace Compass.Bot.Infrastructure.Foundry;

/// <summary>
/// Which hosted agents the bot calls. The Foundry project endpoint itself is shared with every other Foundry client
/// (<c>FoundryProjectOptions</c>, same <c>Bot:Foundry</c> section).
/// </summary>
public sealed class FoundryOptions
{
    public const string SectionName = "Bot:Foundry";

    public HostedAgentReference SharePointRetrievalAgent { get; set; } = new() { Name = "SharePointRetrievalAgent" };

    public HostedAgentReference ZavaApiAgent { get; set; } = new() { Name = "ZavaApiAgent" };
}

/// <summary>A hosted agent by name; leave <see cref="Version"/> empty to follow the latest published version.</summary>
public sealed class HostedAgentReference
{
    public string Name { get; set; } = string.Empty;

    public string? Version { get; set; }
}
