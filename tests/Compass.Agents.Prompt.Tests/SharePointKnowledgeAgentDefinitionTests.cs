using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.Projects;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.Tests.Support;

namespace Compass.Agents.Prompt.Tests;

/// <summary>The security-relevant shape of the published agent: a header placeholder, never a baked-in token.</summary>
public sealed class SharePointKnowledgeAgentDefinitionTests
{
    private static JsonElement PublishedBody(out RecordingHandler handler)
    {
        handler = new RecordingHandler(_ => FoundryWire.Json("""
            { "object": "agent.version", "id": "SharePointKnowledgeAgent:1", "name": "SharePointKnowledgeAgent", "version": "1",
              "created_at": 1, "metadata": {}, "definition": { "kind": "prompt", "model": "gpt-5.6-sol" } }
            """));
        var project = new AIProjectClient(FoundryWire.ProjectEndpoint, new FakeCredential(), new AIProjectClientOptions { Transport = FoundryWire.Transport(handler) });
        new SharePointKnowledgeAgentProvisioner(project, FoundryWire.KnowledgeOptions()).PublishAsync(CancellationToken.None).GetAwaiter().GetResult();
        return JsonDocument.Parse(handler.Requests.Single().Body).RootElement.Clone();
    }

    [Fact]
    public void The_published_tool_binds_the_person_header_to_a_placeholder_not_a_token()
    {
        var body = PublishedBody(out _);

        var tool = body.GetProperty("definition").GetProperty("tools")[0];
        Assert.Equal("mcp", tool.GetProperty("type").GetString());
        Assert.Equal("{{search_auth_token}}", tool.GetProperty("headers").GetProperty("x-ms-query-source-authorization").GetString());
    }

    [Fact]
    public void The_published_tool_uses_the_project_connection_and_only_the_retrieve_tool_without_approval()
    {
        var tool = PublishedBody(out _).GetProperty("definition").GetProperty("tools")[0];

        Assert.Equal("compass-knowledge-base", tool.GetProperty("project_connection_id").GetString());
        Assert.Equal("knowledge_base_retrieve", tool.GetProperty("allowed_tools").GetProperty("tool_names").EnumerateArray().Single().GetString());
        Assert.Equal("never", tool.GetProperty("require_approval").GetString());
    }

    [Fact]
    public void The_search_token_is_a_required_string_structured_input()
    {
        var input = PublishedBody(out _).GetProperty("definition").GetProperty("structured_inputs").GetProperty("search_auth_token");

        Assert.True(input.GetProperty("required").GetBoolean());
        Assert.Equal("string", input.GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public void The_endpoint_must_be_configured()
    {
        Assert.Throws<InvalidOperationException>(() => SharePointKnowledgeAgentDefinition.Build(new KnowledgeAgentOptions()));
    }
}
