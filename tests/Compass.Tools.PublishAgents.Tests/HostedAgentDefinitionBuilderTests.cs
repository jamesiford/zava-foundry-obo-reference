using Azure.AI.Projects.Agents;

namespace Compass.Tools.PublishAgents.Tests;

public sealed class HostedAgentDefinitionBuilderTests
{
    private static HostedAgentSpec Spec(IReadOnlyDictionary<string, string>? env = null) => new(
        "ZavaApiAgent",
        "src/Agents/Compass.Agents.Hosted.ZavaApi/Compass.Agents.Hosted.ZavaApi.csproj",
        "0.5",
        "1Gi",
        env ?? new Dictionary<string, string>());

    [Fact]
    public void Uses_dotnet10_runtime_with_the_assembly_as_entry_point()
    {
        var definition = HostedAgentDefinitionBuilder.Build(Spec());

        Assert.Equal("dotnet_10", definition.CodeConfiguration.Runtime);
        Assert.Equal(["dotnet", "Compass.Agents.Hosted.ZavaApi.dll"], definition.CodeConfiguration.EntryPoint);
    }

    [Fact]
    public void Bundles_dependencies_because_the_tool_publishes_for_linux_itself()
    {
        var definition = HostedAgentDefinitionBuilder.Build(Spec());

        Assert.Equal(CodeDependencyResolution.Bundled, definition.CodeConfiguration.DependencyResolution);
    }

    [Fact]
    public void Speaks_responses_protocol_2_0_0()
    {
        var definition = HostedAgentDefinitionBuilder.Build(Spec());

        var protocol = Assert.Single(definition.Versions);
        Assert.Equal(ProjectsAgentProtocol.Responses, protocol.Protocol);
        Assert.Equal("2.0.0", protocol.Version);
    }

    [Fact]
    public void Carries_the_requested_cpu_and_memory()
    {
        var definition = HostedAgentDefinitionBuilder.Build(Spec());

        Assert.Equal("0.5", definition.Cpu);
        Assert.Equal("1Gi", definition.Memory);
    }

    [Fact]
    public void Copies_environment_variables_onto_the_definition()
    {
        var definition = HostedAgentDefinitionBuilder.Build(
            Spec(new Dictionary<string, string> { ["ZavaApi__McpEndpoint"] = "https://f.example/runtime/webhooks/mcp" }));

        Assert.Equal("https://f.example/runtime/webhooks/mcp", definition.EnvironmentVariables["ZavaApi__McpEndpoint"]);
    }

    [Fact]
    public void Metadata_wraps_the_same_definition_and_describes_the_agent()
    {
        var metadata = HostedAgentDefinitionBuilder.BuildMetadata(Spec());

        Assert.Contains("ZavaApiAgent", metadata.Description);
    }
}
