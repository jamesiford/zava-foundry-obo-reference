using System.Text.Json;

namespace Compass.Tools.PublishAgents;

/// <summary>
/// The record of which version of each agent was last published. Flat <c>{ "AgentName": "version" }</c> on purpose: it is
/// the exact shape of the <c>agentVersions</c> Bicep parameter, so the file can be read back without translation.
/// </summary>
public sealed class AgentVersionManifest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly SortedDictionary<string, string> _versions;

    public AgentVersionManifest(IEnumerable<KeyValuePair<string, string>>? versions = null) =>
        _versions = new SortedDictionary<string, string>(versions?.ToDictionary(p => p.Key, p => p.Value) ?? [], StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Versions => _versions;

    public void Record(string agentName, string version) => _versions[agentName] = version;

    /// <summary>Loads the manifest, or an empty one when it does not exist yet or is unreadable (it is regenerated, never hand-edited).</summary>
    public static AgentVersionManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AgentVersionManifest();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return new AgentVersionManifest(parsed);
        }
        catch (JsonException)
        {
            return new AgentVersionManifest();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(_versions, Json) + Environment.NewLine);
    }

    /// <summary>
    /// The azd environment variables that carry the versions into <c>main.bicep</c>, so a later <c>azd provision</c> keeps the
    /// bot's version settings instead of blanking them.
    /// </summary>
    public IEnumerable<string> ToAzdEnvCommands() =>
        _versions.Select(p => $"azd env set {AzdVariableFor(p.Key)} {p.Value}");

    /// <summary>The bot App Service settings that select a version (<c>Bot:Foundry:&lt;Agent&gt;:Version</c>).</summary>
    public IEnumerable<string> ToBotAppSettings() =>
        _versions.Select(p => $"Bot__Foundry__{p.Key}__Version={p.Value}");

    public static string AzdVariableFor(string agentName) => agentName switch
    {
        HostedAgentCatalog.SharePointRetrievalAgent => "SHAREPOINT_RETRIEVAL_AGENT_VERSION",
        HostedAgentCatalog.ZavaApiAgent => "ZAVA_API_AGENT_VERSION",
        _ => throw new ArgumentOutOfRangeException(nameof(agentName), agentName, "No azd variable is defined for this agent."),
    };
}
