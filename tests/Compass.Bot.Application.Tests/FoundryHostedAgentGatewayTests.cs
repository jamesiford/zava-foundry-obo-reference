using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Responses;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Bot.Infrastructure.Foundry;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Options;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// Contract rule 3 at the wire, through the real Foundry SDK client: what the gateway sends to Foundry, and how it maps
/// what comes back. Only the HTTP transport is fake, so the assertions are on the bytes the SDK actually produces.
/// </summary>
public sealed class FoundryHostedAgentGatewayTests
{
    private const string BotToken = "bot-own-foundry-token";
    private static readonly Uri ProjectEndpoint = new("https://foundry.test/api/projects/compass");

    private sealed record Captured(HttpMethod Method, Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(',', h.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new Captured(request.Method, request.RequestUri!, body, headers));
            return respond();
        }
    }

    private static (FoundryHostedAgentGateway Gateway, StubHandler Handler, FakeCredential Credential) Create(
        Func<HttpResponseMessage> respond,
        FoundryOptions? foundry = null,
        IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>? knowledge = null)
    {
        var handler = new StubHandler(respond);
        var credential = new FakeCredential(BotToken);
        var factory = new FoundryResponsesClientFactory(
            Options.Create(new FoundryProjectOptions { ProjectEndpoint = ProjectEndpoint }),
            credential,
            new HttpClientPipelineTransport(new HttpClient(handler)));
        var gateway = new FoundryHostedAgentGateway(factory, Options.Create(foundry ?? new FoundryOptions()), knowledge ?? new StubKnowledgeAgent());
        return (gateway, handler, credential);
    }

    private static HostedAgentCall ZavaCall(string tokenValue, PersonContext? person = null)
    {
        person ??= TestPeople.UserA;
        return new HostedAgentCall(
            HostedAgentName.ZavaApi,
            person,
            SessionId.Mint(person),
            new Dictionary<string, AccessToken> { [CompassHeaders.ZavaApiToken] = new(tokenValue) });
    }

    private static HttpResponseMessage Ok() => Json(ResponsesWire.CreateResponseBody(
        ResponsesWire.ToText(new MemberBriefingResult("ZCU-1", "Daniel Park", "Active", [], 0m))));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Unauthorized(AgentError error) =>
        Json(ResponsesWire.CreateErrorBody(error), HttpStatusCode.Unauthorized);

    private static Task<MemberBriefingResult> InvokeAsync(FoundryHostedAgentGateway gateway, HostedAgentCall call) =>
        gateway.InvokeAsync<MemberBriefingRequest, MemberBriefingResult>(call, new MemberBriefingRequest("ZCU-1"), CancellationToken.None);

    [Fact]
    public async Task Sends_the_bots_own_token_as_authorization_and_the_person_as_user_identity()
    {
        var (gateway, handler, credential) = Create(Ok);

        await InvokeAsync(gateway, ZavaCall("zava-token"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"Bearer {BotToken}", request.Headers["Authorization"]);
        Assert.        Equal(TestPeople.UserA.ObjectId, request.Headers[CompassHeaders.UserIdentity]);
                Assert.Equal(SessionId.Mint(TestPeople.UserA).Value, request.Headers[CompassHeaders.SessionId]);
        Assert.Contains("ai.azure.com", credential.LastRequest!.Value.Scopes.Single());
    }

    [Fact]
    public async Task Forwards_the_downstream_token_raw_in_its_own_header_and_never_as_authorization()
    {
        var (gateway, handler, _) = Create(Ok);

        await InvokeAsync(gateway, ZavaCall("zava-token"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("zava-token", request.Headers[CompassHeaders.ZavaApiToken]);
        Assert.NotEqual("zava-token", request.Headers["Authorization"]);
        Assert.DoesNotContain("zava-token", request.Body);
    }

    [Fact]
    public async Task Posts_a_responses_call_for_the_named_agent_with_the_typed_request_as_input_and_nothing_stored()
    {
        var (gateway, handler, _) = Create(Ok);

        await InvokeAsync(gateway, ZavaCall("t"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/projects/compass/agents/ZavaApiAgent/endpoint/protocols/openai/responses", request.Uri.AbsolutePath);
        Assert.Equal("?api-version=v1", request.Uri.Query);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("ZavaApiAgent", body.RootElement.GetProperty("agent_reference").GetProperty("name").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.True(ResponsesWire.TryReadInputText(body.RootElement, out var input));
        Assert.Equal("ZCU-1", ResponsesWire.FromText<MemberBriefingRequest>(input)!.MemberNumber);
    }

    [Fact]
    public async Task Returns_the_typed_result_from_the_agents_output_text()
    {
        var (gateway, _, _) = Create(Ok);

        var result = await InvokeAsync(gateway, ZavaCall("t"));

        Assert.Equal("Daniel Park", result.FullName);
    }

    [Fact]
    public async Task Headers_are_per_call_so_one_person_can_never_carry_anothers_identity()
    {
        var (gateway, handler, _) = Create(Ok);

        await InvokeAsync(gateway, ZavaCall("usera-token", TestPeople.UserA));
        await InvokeAsync(gateway, ZavaCall("userb-token", TestPeople.UserB));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TestPeople.UserA.ObjectId, handler.Requests[0].Headers[CompassHeaders.UserIdentity]);
        Assert.Equal("usera-token", handler.Requests[0].Headers[CompassHeaders.ZavaApiToken]);
        Assert.Equal(TestPeople.UserB.ObjectId, handler.Requests[1].Headers[CompassHeaders.UserIdentity]);
        Assert.Equal("userb-token", handler.Requests[1].Headers[CompassHeaders.ZavaApiToken]);
    }

    [Fact]
    public async Task A_pinned_agent_version_is_sent_in_the_agent_reference()
    {
        var (gateway, handler, _) = Create(Ok, new FoundryOptions { ZavaApiAgent = new HostedAgentReference { Name = "ZavaApiAgent", Version = "3" } });

        await InvokeAsync(gateway, ZavaCall("t"));

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("3", body.RootElement.GetProperty("agent_reference").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Maps_a_missing_token_401_to_the_typed_exception()
    {
        var (gateway, _, _) = Create(() => Unauthorized(new AgentError(AgentErrorCodes.MissingToken, "no token")));

        await Assert.ThrowsAsync<MissingDownstreamTokenException>(() => InvokeAsync(gateway, ZavaCall("t")));
    }

    [Fact]
    public async Task Maps_a_claims_challenge_401_and_keeps_the_claims()
    {
        var (gateway, _, _) = Create(() => Unauthorized(new AgentError(AgentErrorCodes.ClaimsChallenge, "step-up", "{\"c\":1}")));

        var ex = await Assert.ThrowsAsync<ClaimsChallengeException>(() => InvokeAsync(gateway, ZavaCall("t")));

        Assert.Equal("{\"c\":1}", ex.Claims);
    }

    // The hosting adapter reports agent failures as HTTP 200 + status:failed, so these are the production shapes.
    private static HttpResponseMessage Failed(AgentError error) => Json(ResponsesWire.CreateFailedResponseBody(error));

    [Fact]
    public async Task Maps_a_failed_response_with_missing_token_to_the_typed_exception()
    {
        var (gateway, _, _) = Create(() => Failed(new AgentError(AgentErrorCodes.MissingToken, "no token")));

        await Assert.ThrowsAsync<MissingDownstreamTokenException>(() => InvokeAsync(gateway, ZavaCall("t")));
    }

    [Fact]
    public async Task Maps_a_failed_response_with_a_claims_challenge_and_keeps_the_claims()
    {
        var (gateway, _, _) = Create(() => Failed(new AgentError(AgentErrorCodes.ClaimsChallenge, "step-up", "{\"c\":1}")));

        var ex = await Assert.ThrowsAsync<ClaimsChallengeException>(() => InvokeAsync(gateway, ZavaCall("t")));

        Assert.Equal("{\"c\":1}", ex.Claims);
    }

    [Fact]
    public async Task Maps_a_failed_response_with_a_rejected_token_to_an_authentication_failure()
    {
        var (gateway, _, _) = Create(() => Failed(new AgentError(AgentErrorCodes.TokenRejected, "rejected")));

        await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => InvokeAsync(gateway, ZavaCall("t")));
    }

    [Fact]
    public async Task Maps_a_failed_response_with_server_error_to_a_502_without_leaking_detail()
    {
        var (gateway, _, _) = Create(() => Failed(new AgentError("server_error", "An internal error occurred.")));

        var ex = await Assert.ThrowsAsync<HostedAgentException>(() => InvokeAsync(gateway, ZavaCall("secret-zava-token")));

        Assert.Equal(502, ex.StatusCode);
        Assert.DoesNotContain("secret-zava-token", ex.ToString());
    }

    [Fact]
    public async Task Treats_a_non_json_401_as_a_downstream_authentication_failure()
    {
        var (gateway, _, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("<html>nope</html>") });

        await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => InvokeAsync(gateway, ZavaCall("t")));
    }

    [Fact]
    public async Task Other_failures_become_hosted_agent_exceptions_with_the_status_code_and_no_token()
    {
        var (gateway, _, _) = Create(() => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var ex = await Assert.ThrowsAsync<HostedAgentException>(() => InvokeAsync(gateway, ZavaCall("secret-zava-token")));

        Assert.Equal(502, ex.StatusCode);
        Assert.DoesNotContain("secret-zava-token", ex.ToString());
    }

    [Fact]
    public async Task A_result_that_is_not_the_agents_json_fails_closed_instead_of_returning_garbage()
    {
        var (gateway, _, _) = Create(() => Json(ResponsesWire.CreateResponseBody("not json")));

        var ex = await Assert.ThrowsAsync<HostedAgentException>(() => InvokeAsync(gateway, ZavaCall("t")));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task An_unconfigured_agent_fails_loudly_instead_of_calling_somewhere_else()
    {
        var (gateway, handler, _) = Create(Ok, new FoundryOptions { ZavaApiAgent = new HostedAgentReference() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(gateway, ZavaCall("t")));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_knowledge_agent_is_called_in_process_with_the_search_token_unchanged_and_no_http_request()
    {
        var knowledge = new StubKnowledgeAgent();
        var (gateway, handler, _) = Create(Ok, knowledge: knowledge);
        var searchToken = FakeTokens.Create("search");
        var call = new HostedAgentCall(
            HostedAgentName.SharePointKnowledge,
            TestPeople.UserA,
            SessionId.Mint(TestPeople.UserA),
            new Dictionary<string, AccessToken> { [CompassHeaders.SearchToken] = new(searchToken) });

        var result = await gateway.InvokeAsync<KnowledgeQueryRequest, KnowledgeQueryResult>(call, new KnowledgeQueryRequest("q"), CancellationToken.None);

        Assert.Equal("stub answer", result.Answer);
        var context = Assert.Single(knowledge.Contexts);
        Assert.Equal(TestPeople.UserA.ObjectId, context.PersonObjectId);
        Assert.Equal(searchToken, context.ForwardedTokens[CompassHeaders.SearchToken]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_knowledge_agent_failure_is_mapped_to_a_status_only_hosted_agent_exception()
    {
        var knowledge = new StubKnowledgeAgent { Respond = (_, _) => throw new KnowledgeAgentException(401, code: null) };
        var (gateway, _, _) = Create(Ok, knowledge: knowledge);
        var call = new HostedAgentCall(
            HostedAgentName.SharePointKnowledge,
            TestPeople.UserA,
            SessionId.Mint(TestPeople.UserA),
            new Dictionary<string, AccessToken> { [CompassHeaders.SearchToken] = new(FakeTokens.Create("search")) });

        var ex = await Assert.ThrowsAsync<HostedAgentException>(() =>
            gateway.InvokeAsync<KnowledgeQueryRequest, KnowledgeQueryResult>(call, new KnowledgeQueryRequest("q"), CancellationToken.None));

        Assert.Equal(HostedAgentName.SharePointKnowledge, ex.Agent);
        Assert.Equal(401, ex.StatusCode);
    }
}
