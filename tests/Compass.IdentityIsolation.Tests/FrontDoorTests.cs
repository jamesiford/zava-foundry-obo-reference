using System.Net;
using System.Text.Json;
using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Bot.Host.FrontDoor;
using Compass.IdentityIsolation.Tests.Support;
using Compass.Shared.Messaging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// The web front door (<c>POST /api/briefing</c>) against the same fake Entra/SharePoint/Zava edges as the Teams cases:
/// the page must reach the same identity decisions Teams does, because it shares the query, validator and OBO broker.
/// </summary>
public sealed class FrontDoorTests
{
    private const string Question = "What should I know about my account and our policies?";

    public static TheoryData<string> RejectedTokens => new() { "expired", "wrong-audience", "wrong-issuer", "forged", "not-a-jwt" };

    [Fact]
    public async Task NoToken_Is401_BeforeAnythingIsAsked()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();

        using var response = await app.GetTestClient().SendAsync(Sse.Post(token: null));

        AssertRejected(response, backend, "Bearer");
        Assert.Equal("sign_in_required", await ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer    ")]
    [InlineData("bearer")]
    public async Task MalformedAuthorizationHeader_Is401(string header)
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();

        using var response = await app.GetTestClient().SendAsync(Sse.Post(header));

        AssertRejected(response, backend, "Bearer");
    }

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task InvalidToken_Is401_AndNoDownstreamTokenIsEverRequested(string kind)
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        var token = kind switch
        {
            "expired" => Mint(backend, TokenState.Expired),
            "wrong-audience" => backend.Tokens.Mint(TestPerson.UserA, "Graph"),
            "wrong-issuer" => Mint(backend, TokenState.WrongIssuer),
            "forged" => FakeJwt.Create("forged-by-attacker"),
            _ => "not-a-jwt",
        };

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer(token));

        AssertRejected(response, backend, "Bearer error=\"invalid_token\"");
        Assert.Equal("sign_in_required", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task EachPersonGetsOnlyTheirOwnBriefing_AcrossBothOrders()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        var client = app.GetTestClient();

        foreach (var person in new[] { TestPerson.UserA, TestPerson.UserB, TestPerson.UserB, TestPerson.UserA })
        {
            var events = await BriefAsync(client, backend, person);
            var answer = events.Single(e => e.Name == "answer").Data;
            var text = answer.GetProperty("answer").GetString()!;

            Assert.Equal(person.DisplayName, answer.GetProperty("displayName").GetString());
            Assert.Contains(person.Canary, text);
            Assert.DoesNotContain(person.ForeignCanary, text);
            Assert.DoesNotContain(person.ForeignName, text);
            Assert.DoesNotContain(person.ForeignCanary, answer.ToString());
        }

        // Whose token each downstream actually used: the caller's, every time, never a shared or app-only one.
        var expected = new[] { TestPerson.UserA, TestPerson.UserB, TestPerson.UserB, TestPerson.UserA }.Select(p => p.ObjectId).ToArray();
        Assert.Equal(expected, backend.ZavaApi.Calls.Select(c => c.TokenOwnerObjectId));
        Assert.Equal(expected, backend.Graph.Calls.Select(c => c.TokenOwnerObjectId));
        Assert.Equal(expected, backend.Knowledge.Calls.Select(c => c.TokenOwnerObjectId));
    }

    [Fact]
    public async Task ProgressArrivesBeforeTheAnswer_AndTheStreamEndsWithEnd()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();

        var events = await BriefAsync(app.GetTestClient(), backend, TestPerson.UserA);

        var names = events.Select(e => e.Name).ToArray();
        Assert.Equal("progress", names[0]);
        Assert.Equal(["answer", "end"], names[^2..]);
        Assert.Single(names, "answer");
        Assert.All(names[..^2], n => Assert.Equal("progress", n));
        Assert.Contains(events, e => e.Name == "progress" && e.Data.GetProperty("stage").GetString() == "branchCompleted"
            && e.Data.GetProperty("outcome").GetString() == "succeeded");
    }

    [Fact]
    public async Task ProgressEvents_CarryOnlyEnumsAndNumbers()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();

        var events = await BriefAsync(app.GetTestClient(), backend, TestPerson.UserA);

        foreach (var progress in events.Where(e => e.Name == "progress"))
        {
            var properties = progress.Data.EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.All(properties, p => Assert.Contains(p, new[] { "stage", "branch", "outcome", "elapsedMilliseconds", "route" }));
            Assert.DoesNotContain(TestPerson.CanaryA, progress.Data.ToString());
            Assert.DoesNotContain(TestPerson.UserA.DisplayName, progress.Data.ToString());
        }
    }

    [Fact]
    public async Task Citations_AreNullOrHttpLinks()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();

        var events = await BriefAsync(app.GetTestClient(), backend, TestPerson.UserA);

        var citations = events.Single(e => e.Name == "answer").Data.GetProperty("citations").EnumerateArray().ToArray();
        Assert.NotEmpty(citations);
        // A knowledge source can have no URL; the page then shows the title without a link.
        Assert.All(citations, c => Assert.True(
            c.GetProperty("url").ValueKind == JsonValueKind.Null
            || System.Text.RegularExpressions.Regex.IsMatch(c.GetProperty("url").GetString()!, "^https?://")));
    }

    [Fact]
    public async Task ClaimsChallengeFromAnAgent_BecomesATypedErrorEvent_WithTheClaims()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);

        var events = await BriefAsync(app.GetTestClient(), backend, TestPerson.UserA);

        Assert.DoesNotContain(events, e => e.Name == "answer");
        var error = events.Single(e => e.Name == "error").Data;
        Assert.Equal("claims_challenge", error.GetProperty("code").GetString());
        Assert.Equal(TokenDirectory.ClaimsChallenge, error.GetProperty("claims").GetString());
        Assert.Equal("end", events[^1].Name);
    }

    [Fact]
    public async Task ExpiredDownstreamToken_BecomesASignInRequiredErrorEvent()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        backend.Tokens.ScriptNext("ZavaApi", TokenState.Expired);

        var events = await BriefAsync(app.GetTestClient(), backend, TestPerson.UserA);

        Assert.DoesNotContain(events, e => e.Name == "answer");
        Assert.Equal("sign_in_required", events.Single(e => e.Name == "error").Data.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NothingTheFrontDoorEmits_ContainsATokenOrAnExceptionMessage()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        var client = app.GetTestClient();
        var wire = new List<string>();

        async Task Capture(HttpRequestMessage request)
        {
            using var response = await client.SendAsync(request);
            wire.Add(await response.Content.ReadAsStringAsync());
            wire.AddRange(response.Headers.SelectMany(h => h.Value.Select(v => $"{h.Key}: {v}")));
        }

        await Capture(Sse.Bearer(backend.Tokens.Mint(TestPerson.UserA, TokenDirectory.SsoAudience), "success A"));
        await Capture(Sse.Bearer(backend.Tokens.Mint(TestPerson.UserB, TokenDirectory.SsoAudience), "success B"));
        backend.Tokens.ScriptNext("Graph", TokenState.Expired);
        await Capture(Sse.Bearer(backend.Tokens.Mint(TestPerson.UserA, TokenDirectory.SsoAudience), "expired downstream"));
        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);
        await Capture(Sse.Bearer(backend.Tokens.Mint(TestPerson.UserB, TokenDirectory.SsoAudience), "challenge"));
        await Capture(Sse.Bearer(backend.Tokens.Mint(TestPerson.UserA, "Graph"), "wrong audience"));
        await Capture(Sse.Post(token: null));

        var captured = backend.CapturedText.Concat(wire).ToArray();
        Assert.Contains(captured, line => line.Contains("failed closed", StringComparison.Ordinal));
        Assert.Contains(wire, line => line.Contains("\"code\":\"claims_challenge\"", StringComparison.Ordinal));

        var leaks = LeakDetector.Find(captured, backend.Tokens.AllTokens);

        Assert.True(leaks.Count == 0, $"{leaks.Count} captured line(s) contained a token. First: {leaks.FirstOrDefault()?[..Math.Min(200, leaks.First().Length)]}");
    }

    [Theory]
    [InlineData("{\"question\":\"hi\",\"memberId\":\"ZCU-100871\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"hi\",\"oid\":\"22222222-2222-2222-2222-222222222222\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"hi\",\"userId\":\"x\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"   \"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("[]", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("not json", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"a\\u0000b\"}", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("{\"question\":\"hi\"}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task InvalidBody_IsRejected_BeforeAnyIdentityWork(string body, string contentType, HttpStatusCode expected)
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        var token = backend.Tokens.Mint(TestPerson.UserA, TokenDirectory.SsoAudience);

        using var response = await app.GetTestClient().SendAsync(Sse.Post("Bearer " + token, body, contentType));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(backend.Broker.Exchanges);
        Assert.Empty(backend.Graph.Calls);
        Assert.Empty(backend.ZavaApi.Calls);
    }

    [Fact]
    public async Task OversizedQuestionAndBody_AreRejected()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync();
        var token = "Bearer " + backend.Tokens.Mint(TestPerson.UserA, TokenDirectory.SsoAudience);

        using var tooLong = await app.GetTestClient().SendAsync(Sse.Post(token, JsonSerializer.Serialize(new { question = new string('q', 501) })));
        using var tooBig = await app.GetTestClient().SendAsync(Sse.Post(token, JsonSerializer.Serialize(new { question = new string('q', 100_000) })));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Empty(backend.Broker.Exchanges);
    }

    [Fact]
    public void TheRequestBody_CanCarryOnlyAQuestion_SoNoIdentityCanBeSupplied()
    {
        var properties = typeof(BriefingRequest).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Question"], properties);
    }

    [Fact]
    public async Task ClaimsChallengeBeforeTheStreamStarts_IsARealHttp401_WithTheChallengeHeader()
    {
        var sender = new ScriptedSender((_, _) => throw new ClaimsChallengeException("Graph", TokenDirectory.ClaimsChallenge));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque-for-this-test"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal($"Bearer error=\"insufficient_claims\", claims=\"{TokenDirectory.ClaimsChallenge}\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("claims_challenge", await ErrorCodeAsync(response));
        Assert.Equal("opaque-for-this-test", sender.ReceivedToken);
    }

    [Fact]
    public async Task ClaimsThatAreNotPlainBase64_AreNeverEchoedIntoAHeader()
    {
        var sender = new ScriptedSender((_, _) => throw new ClaimsChallengeException("Graph", "x\", Bearer realm=\"evil\r\nSet-Cookie: a=b"));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnexpectedFailure_IsA500_WithNoExceptionText()
    {
        var sender = new ScriptedSender((_, _) => throw new InvalidOperationException("secret detail eyJhbGciOi.eyJzdWIi.c2ln"));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("internal_error", await ErrorCodeAsync(response));
        Assert.DoesNotContain("secret detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureMapping_CoversEachKnownCause()
    {
        Assert.Equal((401, "sign_in_required"), Map(new TokenAcquisitionException(DownstreamAudience.Graph, "x")));
        Assert.Equal((403, "member_not_linked"), Map(new MemberNotLinkedException()));
        Assert.Equal((502, "service_unavailable"), Map(new HostedAgentException(HostedAgentName.ZavaApi, 500)));
        Assert.Equal((500, "internal_error"), Map(new InvalidOperationException("x")));
        Assert.Equal((504, "timeout"), Map(new OperationCanceledException(), timedOut: true));
        Assert.Equal((401, "claims_challenge"), Map(new ClaimsChallengeException("Graph", "abc=")));

        static (int, string) Map(Exception ex, bool timedOut = false)
        {
            var failure = FrontDoorFailure.From(ex, timedOut);
            return (failure.Status, failure.Code);
        }
    }

    [Fact]
    public async Task FailureAfterProgress_IsATypedErrorEvent_NotAChangedStatus()
    {
        var sender = new ScriptedSender(async (progress, ct) =>
        {
            await progress!.ReportAsync(new BriefingProgress(BriefingStage.Started, null, null, 1, SharePointRoute.Both), ct);
            throw new ClaimsChallengeException("Graph", TokenDirectory.ClaimsChallenge);
        });
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));
        var events = Sse.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["progress", "error", "end"], events.Select(e => e.Name));
        Assert.Equal("claims_challenge", events[1].Data.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AStepUpDemandFromTheTokenBroker_AfterProgress_IsAClaimsChallengeEventWithBase64Claims()
    {
        const string msalClaims = "{\"access_token\":{\"nbf\":{\"essential\":true}}}";
        var sender = new ScriptedSender(async (progress, ct) =>
        {
            await progress!.ReportAsync(new BriefingProgress(BriefingStage.Started, null, null, 1, SharePointRoute.Both), ct);
            throw new TokenAcquisitionException(DownstreamAudience.Graph, "interaction required", msalClaims);
        });
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));
        var events = Sse.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var error = events.Single(e => e.Name == "error").Data;
        Assert.Equal("claims_challenge", error.GetProperty("code").GetString());
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(msalClaims)), error.GetProperty("claims").GetString());
    }

    [Fact]
    public async Task AStepUpDemandFromTheTokenBroker_BeforeTheStream_IsAReal401WithTheChallengeHeader()
    {
        const string msalClaims = "{\"access_token\":{\"nbf\":{\"essential\":true}}}";
        var sender = new ScriptedSender((_, _) => throw new TokenAcquisitionException(DownstreamAudience.Graph, "interaction required", msalClaims));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(msalClaims)), response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal("claims_challenge", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ABrokerFailureWithoutClaims_StaysASignInRequired()
    {
        var sender = new ScriptedSender((_, _) => throw new TokenAcquisitionException(DownstreamAudience.Graph, "failed"));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("sign_in_required", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ABrokerStepUpDemandThatIsNotJson_IsNeverEchoedIntoAHeader()
    {
        var sender = new ScriptedSender((_, _) => throw new TokenAcquisitionException(DownstreamAudience.Graph, "x", "a\", ******\"evil\r\nSet-Cookie: a=b"));
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(sender));

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("evil", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABriefingThatRunsPastTheDeadline_Is504()
    {
        var sender = new ScriptedSender(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        await using var app = await FrontDoorHost.StartAsync(
            s => s.AddSingleton<ISender>(sender),
            b => b.Configuration["Bot:FrontDoor:TurnDeadline"] = "00:00:00.150");

        using var response = await app.GetTestClient().SendAsync(Sse.Bearer("opaque"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("timeout", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task WhenDisabled_NeitherEndpointExists()
    {
        await using var backend = await FakeBackend.StartAsync();
        await using var app = await backend.StartFrontDoorAsync(b => b.Configuration["Bot:FrontDoor:Enabled"] = "false");
        var client = app.GetTestClient();

        using var briefing = await client.SendAsync(Sse.Bearer("anything"));
        using var config = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.NotFound, briefing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, config.StatusCode);
    }

    [Fact]
    public async Task Config_ExposesOnlyPublicSignInValues()
    {
        await using var app = await FrontDoorHost.StartAsync(s => s.AddSingleton<ISender>(new ScriptedSender((_, _) => throw new InvalidOperationException())));

        using var response = await app.GetTestClient().GetAsync("/api/config");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["authority", "clientId", "scope", "tenantId"], json.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal($"api://botid-{FrontDoorHost.FakeClientId}/access_as_user", json.GetProperty("scope").GetString());
        Assert.Equal($"https://login.microsoftonline.com/{FrontDoorHost.FakeTenantId}", json.GetProperty("authority").GetString());
    }

    [Fact]
    public async Task Config_IsNotConfigured_WhenTheValuesAreStillPlaceholders()
    {
        await using var app = await FrontDoorHost.StartAsync(
            s => s.AddSingleton<ISender>(new ScriptedSender((_, _) => throw new InvalidOperationException())),
            b => b.Services.PostConfigure<Compass.Bot.Infrastructure.Obo.OnBehalfOfOptions>(o => o.ClientId = "<bot-app-client-id>"));

        using var response = await app.GetTestClient().GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static string Mint(FakeBackend backend, TokenState state)
    {
        backend.Tokens.ScriptNext(TokenDirectory.SsoAudience, state);
        return backend.Tokens.Mint(TestPerson.UserA, TokenDirectory.SsoAudience);
    }

    private static async Task<IReadOnlyList<SseEvent>> BriefAsync(HttpClient client, FakeBackend backend, TestPerson person)
    {
        var token = backend.Tokens.Mint(person, TokenDirectory.SsoAudience);

        using var response = await client.SendAsync(Sse.Bearer(token, Question));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        return Sse.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertRejected(HttpResponseMessage response, FakeBackend backend, string challenge)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith(challenge, response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(backend.Broker.Exchanges);
        Assert.Empty(backend.Graph.Calls);
        Assert.Empty(backend.ZavaApi.Calls);
        Assert.Empty(backend.Knowledge.Calls);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString();

    /// <summary>An <see cref="ISender"/> that runs a script instead of the real query, recording the token it was handed.</summary>
    private sealed class ScriptedSender(Func<IBriefingProgressSink?, CancellationToken, Task<MemberBriefing>> script) : ISender
    {
        public string? ReceivedToken { get; private set; }

        public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
        {
            var query = (GetMemberBriefingQuery)(object)request;
            ReceivedToken = query.SsoToken.Reveal();
            return (TResponse)(object)await script(query.Progress, cancellationToken);
        }
    }
}
