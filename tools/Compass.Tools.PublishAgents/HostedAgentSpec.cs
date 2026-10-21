namespace Compass.Tools.PublishAgents;

/// <summary>
/// Everything needed to publish one hosted agent. One spec per agent keeps the "one class, one hosted agent" rule
/// visible in the publish path too: each agent is its own project, its own zip and its own Foundry version history.
/// </summary>
/// <param name="Name">Foundry agent name. Must equal the name the bot is configured with, or the bot will invoke nothing.</param>
/// <param name="ProjectPath">Path of the agent's csproj relative to the repository root.</param>
/// <param name="Cpu">Sandbox CPU tier.</param>
/// <param name="Memory">Sandbox memory tier; must be a valid pair with <paramref name="Cpu"/>.</param>
/// <param name="EnvironmentVariables">
/// Container environment. Foundry treats it as immutable per version, so changing a value means publishing a new version.
/// </param>
public sealed record HostedAgentSpec(
    string Name,
    string ProjectPath,
    string Cpu,
    string Memory,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    /// <summary>
    /// The published assembly. The platform starts the container with <c>dotnet &lt;assembly&gt;.dll</c>, so this is the entry point.
    /// </summary>
    public string AssemblyName => Path.GetFileNameWithoutExtension(ProjectPath);
}
