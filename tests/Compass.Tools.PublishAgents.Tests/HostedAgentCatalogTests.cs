namespace Compass.Tools.PublishAgents.Tests;

public sealed class HostedAgentCatalogTests
{
    private static PublishSettings Settings(string? mcp = null, bool whatIf = false, params string[] only) =>
        new("https://p.example", mcp, "root", "manifest.json", new HashSet<string>(only, StringComparer.OrdinalIgnoreCase), whatIf);

    [Fact]
    public void Publishes_both_agents_by_default()
    {
        var specs = HostedAgentCatalog.Create(Settings("https://f.example"));

        Assert.Equal(["SharePointRetrievalAgent", "ZavaApiAgent"], specs.Select(s => s.Name));
    }

    [Fact]
    public void Zava_agent_gets_the_full_mcp_webhook_url_from_the_bare_host()
    {
        var spec = HostedAgentCatalog.Create(Settings("https://f.example/")).Single(s => s.Name == "ZavaApiAgent");

        Assert.Equal("https://f.example/runtime/webhooks/mcp", spec.EnvironmentVariables["ZavaApi__McpEndpoint"]);
    }

    [Fact]
    public void Does_not_double_the_webhook_path_when_it_is_already_present()
    {
        Assert.Equal(
            "https://f.example/runtime/webhooks/mcp",
            HostedAgentCatalog.ToMcpWebhookUrl("https://f.example/runtime/webhooks/mcp/"));
    }

    [Fact]
    public void SharePoint_agent_container_has_no_settings()
    {
        var spec = HostedAgentCatalog.Create(Settings("https://f.example")).Single(s => s.Name == "SharePointRetrievalAgent");

        Assert.Empty(spec.EnvironmentVariables);
    }

    [Fact]
    public void A_real_run_without_the_mcp_endpoint_refuses_rather_than_mint_a_broken_immutable_version()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostedAgentCatalog.Create(Settings()));

        Assert.Contains("SERVICE_MCP_ENDPOINT", ex.Message);
    }

    [Fact]
    public void What_if_uses_a_placeholder_when_the_endpoint_is_unknown()
    {
        var spec = HostedAgentCatalog.Create(Settings(whatIf: true)).Single(s => s.Name == "ZavaApiAgent");

        Assert.Equal(HostedAgentCatalog.McpEndpointPlaceholder, spec.EnvironmentVariables["ZavaApi__McpEndpoint"]);
    }

    [Fact]
    public void Publishing_only_the_SharePoint_agent_does_not_need_the_mcp_endpoint()
    {
        var specs = HostedAgentCatalog.Create(Settings(only: "SharePointRetrievalAgent"));

        Assert.Equal("SharePointRetrievalAgent", Assert.Single(specs).Name);
    }

    [Fact]
    public void Spec_paths_point_at_real_projects_and_entry_assemblies_match_project_names()
    {
        var root = CommandLine.FindRepositoryRoot(AppContext.BaseDirectory)!;

        foreach (var spec in HostedAgentCatalog.Create(Settings("https://f.example")))
        {
            Assert.True(File.Exists(Path.Combine(root, spec.ProjectPath)), spec.ProjectPath);
            Assert.StartsWith("Compass.Agents.Hosted.", spec.AssemblyName);
        }
    }
}
