using System.Text.Json;
using Azure.AI.Projects;
using Compass.Agents.Prompt.LearnDocs;
using Compass.Agents.Prompt.Tests.Support;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.Tests;

/// <summary>The published Learn agent is one public MCP tool and nothing else: no connection, no headers, no inputs.</summary>
public sealed class LearnDocsAgentDefinitionTests
{
    private static JsonElement PublishedDefinition(LearnDocsAgentOptions? options = null)
    {
        var handler = new RecordingHandler(_ => FoundryWire.Json("""
            { "object": "agent.version", "id": "LearnDocsAgent:1", "name": "LearnDocsAgent", "version": "1",
              "created_at": 1, "metadata": {}, "definition": { "kind": "prompt", "model": "gpt-5.6-sol" } }
            """));
        var project = new AIProjectClient(FoundryWire.ProjectEndpoint, new FakeCredential(), new AIProjectClientOptions { Transport = FoundryWire.Transport(handler) });
        new LearnDocsAgentProvisioner(project, Options.Create(options ?? new LearnDocsAgentOptions())).PublishAsync(CancellationToken.None).GetAwaiter().GetResult();
        return JsonDocument.Parse(handler.Requests.Single().Body).RootElement.GetProperty("definition").Clone();
    }

    [Fact]
    public void The_only_tool_is_an_mcp_tool()
    {
        var tool = PublishedDefinition().GetProperty("tools").EnumerateArray().Single();

        Assert.Equal("mcp", tool.GetProperty("type").GetString());
    }

    [Fact]
    public void The_server_uri_and_label_come_from_the_options()
    {
        var options = new LearnDocsAgentOptions { McpServerUri = new Uri("https://docs.test/mcp"), ServerLabel = "docs-test" };

        var tool = PublishedDefinition(options).GetProperty("tools")[0];

        Assert.Equal("https://docs.test/mcp", tool.GetProperty("server_url").GetString());
        Assert.Equal("docs-test", tool.GetProperty("server_label").GetString());
    }

    [Fact]
    public void The_defaults_point_at_the_public_learn_server()
    {
        var tool = PublishedDefinition().GetProperty("tools")[0];

        Assert.Equal("https://learn.microsoft.com/api/mcp", tool.GetProperty("server_url").GetString());
        Assert.Equal("microsoft-learn", tool.GetProperty("server_label").GetString());
    }

    [Fact]
    public void No_approval_is_required()
    {
        var tool = PublishedDefinition().GetProperty("tools")[0];

        Assert.Equal("never", tool.GetProperty("require_approval").GetString());
    }

    [Fact]
    public void The_server_is_public_so_there_is_no_connection_headers_or_structured_inputs()
    {
        var definition = PublishedDefinition();
        var tool = definition.GetProperty("tools")[0];

        Assert.False(tool.TryGetProperty("project_connection_id", out _));
        Assert.False(tool.TryGetProperty("headers", out var headers) && headers.EnumerateObject().Any());
        Assert.False(definition.TryGetProperty("structured_inputs", out var inputs) && inputs.EnumerateObject().Any());
    }

    [Fact]
    public void The_model_deployment_comes_from_the_options()
    {
        var definition = PublishedDefinition(new LearnDocsAgentOptions { ModelDeployment = "other-model" });

        Assert.Equal("other-model", definition.GetProperty("model").GetString());
    }

    [Fact]
    public void The_instructions_name_the_learn_tool_and_require_cited_urls()
    {
        var instructions = PublishedDefinition().GetProperty("instructions").GetString();

        Assert.Contains("Microsoft Learn tool", instructions);
        Assert.Contains("Cite the Learn page URLs", instructions);
    }
}
