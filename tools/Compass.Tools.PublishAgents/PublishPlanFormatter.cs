using System.Text;

namespace Compass.Tools.PublishAgents;

/// <summary>Describes what a run will do without doing it, so Scout can review before anything touches the tenant.</summary>
public static class PublishPlanFormatter
{
    public static string Format(IReadOnlyList<HostedAgentSpec> specs, PublishSettings settings)
    {
        var text = new StringBuilder();
        text.AppendLine("Publish plan (nothing is built, uploaded or created in what-if mode)");
        text.AppendLine($"  Foundry project : {settings.ProjectEndpoint ?? "<FOUNDRY_PROJECT_ENDPOINT>"}");
        text.AppendLine($"  Manifest        : {settings.ManifestPath}");
        text.AppendLine();

        foreach (var spec in specs)
        {
            var definition = HostedAgentDefinitionBuilder.Build(spec);
            text.AppendLine($"Agent {spec.Name}");
            text.AppendLine($"  1. dotnet publish {spec.ProjectPath} -c Release -r linux-x64 --self-contained false");
            text.AppendLine($"  2. create a new version from the publish folder (the SDK zips it)");
            text.AppendLine($"       runtime            : {definition.CodeConfiguration.Runtime}");
            text.AppendLine($"       entry point        : {string.Join(' ', definition.CodeConfiguration.EntryPoint)}");
            text.AppendLine($"       dependencies       : bundled");
            text.AppendLine($"       protocol           : responses {definition.Versions[0].Version}");
            text.AppendLine($"       cpu / memory       : {definition.Cpu} / {definition.Memory}");
            text.AppendLine(spec.EnvironmentVariables.Count == 0
                ? "       environment        : (none)"
                : $"       environment        : {string.Join(", ", spec.EnvironmentVariables.Select(p => $"{p.Key}={p.Value}"))}");
            text.AppendLine("  3. poll until the version is active (fails the run if it reports failed)");
            text.AppendLine();
        }

        text.AppendLine("Then: record the versions in the manifest and print the azd / App Service settings that point the bot at them.");
        return text.ToString();
    }
}
