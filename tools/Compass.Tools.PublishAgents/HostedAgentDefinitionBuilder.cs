using Azure.AI.Projects.Agents;

namespace Compass.Tools.PublishAgents;

/// <summary>
/// Maps a <see cref="HostedAgentSpec"/> onto the Foundry definition. Isolated so the one thing that is easy to get wrong
/// silently (runtime, entry point, protocol, resolution mode) is covered by tests instead of discovered at provisioning time.
/// </summary>
public static class HostedAgentDefinitionBuilder
{
    /// <summary>Runtime must match the <c>TargetFramework</c> of the published output.</summary>
    public const string Runtime = "dotnet_10";

    /// <summary>The Responses protocol version the hosting adapter speaks.</summary>
    public const string ResponsesProtocolVersion = "2.0.0";

    /// <summary>
    /// Bundled resolution: we publish for linux-x64 ourselves, so the platform runs the zip as-is. That avoids a server-side
    /// restore and makes the deployed bits identical to what was built and tested here.
    /// </summary>
    public static HostedAgentDefinition Build(HostedAgentSpec spec)
    {
        var definition = new HostedAgentDefinition(spec.Cpu, spec.Memory)
        {
            CodeConfiguration = new CodeConfiguration(
                runtime: Runtime,
                entryPoint: ["dotnet", spec.AssemblyName + ".dll"],
                dependencyResolution: CodeDependencyResolution.Bundled),
        };
        definition.Versions.Add(new ProtocolVersionRecord(ProjectsAgentProtocol.Responses, ResponsesProtocolVersion));

        foreach (var (name, value) in spec.EnvironmentVariables)
        {
            definition.EnvironmentVariables[name] = value;
        }

        return definition;
    }

    public static AgentVersionFromCodeMetadata BuildMetadata(HostedAgentSpec spec) =>
        new(Build(spec)) { Description = $"{spec.Name} (Compass OBO reference) - published from source by Compass.Tools.PublishAgents." };
}
