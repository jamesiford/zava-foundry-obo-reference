namespace Compass.Tools.PublishAgents.Tests;

public sealed class CommandLineTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot() =>
        CommandLine.FindRepositoryRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Run tests from inside the repository.");

    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        key => values.Where(v => v.Key == key).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void What_if_needs_no_endpoint_and_no_environment()
    {
        var result = CommandLine.Parse(["--what-if"], Env(), Root);

        Assert.NotNull(result.Settings);
        Assert.True(result.Settings.WhatIf);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Agent365_export_is_off_unless_the_flag_is_passed()
    {
        Assert.False(CommandLine.Parse(["--what-if"], Env(), Root).Settings!.Agent365Enabled);
        Assert.True(CommandLine.Parse(["--what-if", "--agent365"], Env(), Root).Settings!.Agent365Enabled);
    }

    [Fact]
    public void A_real_run_without_a_project_endpoint_fails_with_a_helpful_message()
    {
        var result = CommandLine.Parse([], Env(), Root);

        Assert.Null(result.Settings);
        Assert.Contains("FOUNDRY_PROJECT_ENDPOINT", result.Error);
    }

    [Fact]
    public void Reads_endpoints_from_the_azd_environment_variables()
    {
        var result = CommandLine.Parse([], Env(
            (CommandLine.ProjectEndpointVariable, "https://p.example/api/projects/x"),
            (CommandLine.McpEndpointVariable, "https://f.example")), Root);

        Assert.Equal("https://p.example/api/projects/x", result.Settings!.ProjectEndpoint);
        Assert.Equal("https://f.example", result.Settings.ZavaApiMcpEndpoint);
    }

    [Fact]
    public void Arguments_override_the_environment()
    {
        var result = CommandLine.Parse(["--project-endpoint", "https://arg.example"], Env(
            (CommandLine.ProjectEndpointVariable, "https://env.example")), Root);

        Assert.Equal("https://arg.example", result.Settings!.ProjectEndpoint);
    }

    [Fact]
    public void Agent_filter_is_repeatable_and_case_insensitive()
    {
        var result = CommandLine.Parse(["--what-if", "--agent", "zavaapiagent", "--agent", "SharePointRetrievalAgent"], Env(), Root);

        Assert.Equal(2, result.Settings!.Only.Count);
        Assert.Contains("ZavaApiAgent", result.Settings.Only);
    }

    [Fact]
    public void Rejects_an_unknown_agent_name()
    {
        var result = CommandLine.Parse(["--what-if", "--agent", "Nope"], Env(), Root);

        Assert.Contains("Unknown agent 'Nope'", result.Error);
    }

    [Fact]
    public void Rejects_an_unknown_argument_and_a_missing_value()
    {
        Assert.Contains("Unknown argument", CommandLine.Parse(["--bogus"], Env(), Root).Error);
        Assert.Contains("needs a value", CommandLine.Parse(["--agent"], Env(), Root).Error);
    }

    [Fact]
    public void Help_is_recognised()
    {
        Assert.True(CommandLine.Parse(["--help"], Env(), Root).ShowHelp);
    }

    [Fact]
    public void Default_manifest_lives_under_the_gitignored_generated_folder()
    {
        var result = CommandLine.Parse(["--what-if"], Env(), Root);

        Assert.Equal(Path.Combine(Root, "infra", ".generated", "agent-versions.json"), result.Settings!.ManifestPath);
    }
}
