using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.Tests.Support;

/// <summary>A request as it left the SDK, captured at the HTTP boundary so tests assert the real wire format.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>Records every outgoing request and replies with a scripted one, so no test needs a Foundry project.</summary>
internal sealed class RecordingHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(',', h.Value), StringComparer.OrdinalIgnoreCase);
        var captured = new CapturedRequest(request.Method, request.RequestUri!, body, headers);
        Requests.Add(captured);
        return respond(captured);
    }
}

internal sealed class FakeCredential(string token = "bot-managed-identity-token") : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

internal static class FoundryWire
{
    public static readonly Uri ProjectEndpoint = new("https://foundry.test/api/projects/compass");

    public static HttpClientPipelineTransport Transport(RecordingHandler handler) => new(new HttpClient(handler));

    public static IOptions<FoundryProjectOptions> ProjectOptions() => Options.Create(new FoundryProjectOptions { ProjectEndpoint = ProjectEndpoint });

    public static IOptions<KnowledgeAgentOptions> KnowledgeOptions() => Options.Create(new KnowledgeAgentOptions
    {
        KnowledgeBaseMcpEndpoint = new Uri("https://search.test/knowledgebases/zava-kb/mcp?api-version=2026-08-01-preview"),
    });

    public static FoundryResponsesClientFactory Factory(RecordingHandler handler) =>
        new(ProjectOptions(), new FakeCredential(), Transport(handler));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage CompletedResponse(string text) => Json($$"""
        {
          "id": "resp_1", "object": "response", "created_at": 1, "status": "completed", "model": "gpt",
          "output": [{ "type": "message", "id": "msg_1", "status": "completed", "role": "assistant",
            "content": [{ "type": "output_text", "text": {{JsonSerializer.Serialize(text)}}, "annotations": [] }] }]
        }
        """);

    /// <summary>A completed response whose message carries Responses <c>url_citation</c> annotations, as Foundry returns them.</summary>
    public static HttpResponseMessage CompletedResponseWithCitations(string text, params (string Url, string Title)[] citations)
    {
        var annotations = string.Join(",", citations.Select(c =>
            $"{{\"type\":\"url_citation\",\"url\":{JsonSerializer.Serialize(c.Url)},\"start_index\":0,\"end_index\":1,\"title\":{JsonSerializer.Serialize(c.Title)}}}"));
        return Json($$"""
            {
              "id": "resp_1", "object": "response", "created_at": 1, "status": "completed", "model": "gpt",
              "output": [{ "type": "message", "id": "msg_1", "status": "completed", "role": "assistant",
                "content": [{ "type": "output_text", "text": {{JsonSerializer.Serialize(text)}}, "annotations": [{{annotations}}] }] }]
            }
            """);
    }
}