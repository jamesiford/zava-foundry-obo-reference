using System.Net;
using System.Text.Json;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Responses;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Agents.Hosted.SharePointRetrieval;
using Compass.Agents.Hosting;
using Compass.Shared.Identity.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Compass.Agents.Contracts.Tests.Hosting;

/// <summary>
/// Drives the real Foundry Responses hosting adapter (dev spec §9 hop 4) in memory: forwarded <c>x-client-*</c> headers
/// in, typed result or typed failure out, and nothing sensitive in the logs or bodies. Nothing here fakes the adapter, so
/// these tests pin what the platform does to an agent's errors.
/// </summary>
public sealed class HostedAgentResponsesTests : IAsyncLifetime
{
    private readonly FakeGraphRetrievalClient _graph = new();
    private readonly FakeLoggerProvider _logs = new();
    private readonly RecordingAgent _agent;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public HostedAgentResponsesTests() => _agent = new RecordingAgent(new SharePointRetrievalAgent(_graph));

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        builder.Services.AddSingleton<IHostedAgent<RetrievalRequest, RetrievalResult>>(_agent);
        builder.Services.AddHostedAgentResponses<SharePointRetrievalResponseHandler>();

        _app = builder.Build();
        _app.MapHostedAgentResponses();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> PostAsync(string? graphToken, string? person = "oid-user-a", string? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/responses")
        {
            Content = new StringContent(
                body ?? ResponsesWire.CreateRequestBody("SharePointRetrievalAgent", new RetrievalRequest("lending policy")),
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        if (person is not null)
        {
            request.Headers.Add(CompassHeaders.AgentUserId, person);
        }

        if (graphToken is not null)
        {
            request.Headers.Add(CompassHeaders.GraphAccessToken, graphToken);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Forwards_the_x_client_header_to_the_agent_and_returns_the_typed_response()
    {
        var token = FakeTokens.Create("http-user-a");
        _graph.Result = new RetrievalResult([new RetrievalHit("https://c/x", "T", "text", 1)]);

        var response = await PostAsync(token);
        var json = await response.Content.ReadAsStringAsync();
        var body = ResponsesWire.FromText<RetrievalResult>(ResponsesWire.ReadOutputText(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("T", body!.Hits.Single().Title);
        Assert.Equal(token, Assert.Single(_graph.Calls).Token);
    }

    [Fact]
    public async Task Builds_the_call_context_from_the_forwarded_headers()
    {
        var token = FakeTokens.Create("ctx");

        await PostAsync(token, person: "oid-userb");

        var context = Assert.Single(_agent.Contexts);
        Assert.Equal("oid-userb", context.PersonObjectId);
        Assert.Equal(token, context.ForwardedTokens[CompassHeaders.GraphAccessToken]);
    }

    [Fact]
    public async Task Missing_token_fails_closed_as_a_typed_failed_response_and_never_reaches_the_downstream()
    {
        var response = await PostAsync(graphToken: null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal("failed", Status(json));
        Assert.Equal(AgentErrorCodes.MissingToken, ResponsesWire.ReadError(json)!.Code);
        Assert.Empty(_graph.Calls);
    }

    [Fact]
    public async Task Claims_challenge_survives_the_adapter_so_the_bot_can_re_authenticate()
    {
        _graph.Failure = new ClaimsChallengeException("Graph", "eyJhY2Nlc3NfdG9rZW4iOnt9fQ");

        var json = await (await PostAsync(FakeTokens.Create("t"))).Content.ReadAsStringAsync();
        var error = ResponsesWire.ReadError(json)!;

        Assert.Equal("failed", Status(json));
        Assert.Equal(AgentErrorCodes.ClaimsChallenge, error.Code);
        Assert.Equal("eyJhY2Nlc3NfdG9rZW4iOnt9fQ", error.Claims);
    }

    [Fact]
    public async Task Rejected_token_is_reported_as_token_rejected()
    {
        _graph.Failure = new DownstreamAuthenticationException("Graph", 401);

        var json = await (await PostAsync(FakeTokens.Create("t"))).Content.ReadAsStringAsync();

        Assert.Equal(AgentErrorCodes.TokenRejected, ResponsesWire.ReadError(json)!.Code);
    }

    [Fact]
    public async Task An_unexpected_exception_is_flattened_so_internals_never_leak()
    {
        _graph.Failure = new InvalidOperationException("secret-internal-detail");

        var json = await (await PostAsync(FakeTokens.Create("t"))).Content.ReadAsStringAsync();

        Assert.DoesNotContain("secret-internal-detail", json);
        Assert.Equal("failed", Status(json));
        Assert.Equal("server_error", ResponsesWire.ReadError(json)!.Code);
    }

    [Fact]
    public async Task Input_that_is_not_the_agents_json_request_is_rejected_without_calling_the_agent()
    {
        var response = await PostAsync(FakeTokens.Create("t"), body: """{"input":"hello there","stream":false}""");

        Assert.NotEqual("completed", Status(await response.Content.ReadAsStringAsync()));
        Assert.Empty(_graph.Calls);
    }

    [Fact]
    public async Task Neither_the_logs_nor_the_bodies_contain_the_token_on_any_path()
    {
        var token = FakeTokens.Create("log-scan");

        var success = await PostAsync(token);
        _graph.Failure = new DownstreamAuthenticationException("Graph", 401);
        var rejected = await PostAsync(token);
        var missing = await PostAsync(graphToken: null);

        var logged = string.Join('\n', _logs.Collector.GetSnapshot().Select(r => r.Message + r.Exception));
        Assert.DoesNotContain(token, logged);
        Assert.DoesNotContain(token, await success.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, await rejected.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, await missing.Content.ReadAsStringAsync());
        Assert.NotEmpty(_logs.Collector.GetSnapshot());
    }

    private static string? Status(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
    }

    private sealed class RecordingAgent(IHostedAgent<RetrievalRequest, RetrievalResult> inner) : IHostedAgent<RetrievalRequest, RetrievalResult>
    {
        public List<AgentCallContext> Contexts { get; } = [];

        public Task<RetrievalResult> HandleAsync(RetrievalRequest request, AgentCallContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return inner.HandleAsync(request, context, ct);
        }
    }
}
