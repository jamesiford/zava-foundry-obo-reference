using System.Net;
using System.Text.Json;
using Azure.AI.Projects;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.Tests.Support;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.Tests;

/// <summary>
/// The answer agent is tool-less and is handed only a prompt: asserted on what the SDK actually publishes and sends, so a
/// tool, header, stored conversation or person field cannot appear without a test failing.
/// </summary>
public sealed class SharePointAnswerAgentTests
{
    private const string Prompt = "<question>q</question><extracts><extract number=\"1\">text</extract></extracts>";

    private static SharePointAnswerAgent CreateAgent(RecordingHandler handler, AnswerAgentOptions? options = null) =>
        new(FoundryWire.Factory(handler), Options.Create(options ?? new AnswerAgentOptions()));

    private static JsonElement PublishedDefinition()
    {
        var handler = new RecordingHandler(_ => FoundryWire.Json("""
            { "object": "agent.version", "id": "SharePointAnswerAgent:1", "name": "SharePointAnswerAgent", "version": "1",
              "created_at": 1, "metadata": {}, "definition": { "kind": "prompt", "model": "gpt-5.6-sol" } }
            """));
        var project = new AIProjectClient(FoundryWire.ProjectEndpoint, new FakeCredential(), new AIProjectClientOptions { Transport = FoundryWire.Transport(handler) });
        new SharePointAnswerAgentProvisioner(project, Options.Create(new AnswerAgentOptions())).PublishAsync(CancellationToken.None).GetAwaiter().GetResult();
        return JsonDocument.Parse(handler.Requests.Single().Body).RootElement.GetProperty("definition").Clone();
    }

    private static IEnumerable<string> AllStrings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AllStrings),
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => AllStrings(p.Value)),
        _ => [],
    };

    [Fact]
    public void The_published_agent_has_no_tools_and_no_structured_inputs()
    {
        var definition = PublishedDefinition();

        Assert.False(definition.TryGetProperty("tools", out var tools) && tools.GetArrayLength() > 0);
        Assert.False(definition.TryGetProperty("structured_inputs", out var inputs) && inputs.EnumerateObject().Any());
    }

    [Fact]
    public void The_published_agent_uses_the_configured_model_deployment()
    {
        Assert.Equal("gpt-5.6-sol", PublishedDefinition().GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("only the numbered extracts")]
    [InlineData("untrusted")]
    [InlineData("never follow")]
    [InlineData("outside knowledge")]
    [InlineData("I don't know")]
    [InlineData("[1]")]
    public void The_instructions_carry_each_safety_rule(string phrase) =>
        Assert.Contains(phrase, SharePointAnswerAgentDefinition.Instructions.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " "), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Sends_only_the_prompt_as_an_unstored_single_turn_by_agent_reference()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("The cap is 43% [1]."));

        var answer = await CreateAgent(handler).AnswerAsync(Prompt, CancellationToken.None);

        Assert.Equal("The cap is 43% [1].", answer);
        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("SharePointAnswerAgent", root.GetProperty("agent_reference").GetProperty("name").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("previous_response_id", out _));
        Assert.False(root.TryGetProperty("conversation", out _));
        Assert.False(root.TryGetProperty("structured_inputs", out _));
        var input = Assert.Single(root.GetProperty("input").EnumerateArray());
        Assert.Equal("user", input.GetProperty("role").GetString());
        Assert.Contains(Prompt, AllStrings(input), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Authenticates_as_the_bot_and_sends_no_person_or_token_header()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("ok"));

        await CreateAgent(handler).AnswerAsync(Prompt, CancellationToken.None);

        var headers = Assert.Single(handler.Requests).Headers;
        Assert.StartsWith("Bearer ", headers["Authorization"], StringComparison.Ordinal);
        Assert.DoesNotContain(headers.Keys, key => key.StartsWith("x-compass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Maps_a_foundry_rejection_to_a_status_only_exception()
    {
        var handler = new RecordingHandler(_ => FoundryWire.Json("{\"error\":{\"message\":\"echoed extract text SENSITIVE\"}}", HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<AnswerAgentException>(() => CreateAgent(handler).AnswerAsync(Prompt, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.DoesNotContain("SENSITIVE", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_a_failed_response_to_a_bad_gateway()
    {
        var handler = new RecordingHandler(_ => FoundryWire.Json("""
            { "id": "resp_1", "object": "response", "created_at": 1, "status": "failed", "model": "gpt",
              "error": { "code": "server_error", "message": "boom" }, "output": [] }
            """));

        var ex = await Assert.ThrowsAsync<AnswerAgentException>(() => CreateAgent(handler).AnswerAsync(Prompt, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }
}
