using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Responses;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Host.Turns;
using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// Case 3 (dev spec §12): across successful and failing runs, no token — by shape or by exact value — appears in
/// anything the system logged, in any exception text or in any HTTP error body.
/// </summary>
public sealed class TokenLeakTests
{
    [Fact]
    public async Task NoTokenAppearsInLogsExceptionsOrResponseBodies_AcrossSuccessAndFailure()
    {
        await using var backend = await FakeBackend.StartAsync();

        await backend.AskAsync(TestPerson.UserA, "success A");
        await backend.AskAsync(TestPerson.UserB, "success B");

        backend.Tokens.ScriptNext("Graph", TokenState.Expired);
        backend.Observe(await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => backend.BriefAsync(TestPerson.UserA, "expired")));

        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);
        backend.Observe(await Assert.ThrowsAsync<ClaimsChallengeException>(() => backend.BriefAsync(TestPerson.UserB, "challenge")));

        backend.Tokens.ScriptNext("ZavaApi", TokenState.Expired);
        await backend.TurnAsync(TestPerson.UserA, "expired via turn handler");

        using (var response = await backend.SharePointAgentClient.PostAsync("/responses", new StringContent(ResponsesWire.CreateRequestBody("SharePointRetrievalAgent", new RetrievalRequest("no token")), System.Text.Encoding.UTF8, "application/json")))
        {
            Assert.Equal(AgentErrorCodes.MissingToken, ResponsesWire.ReadError(await response.Content.ReadAsStringAsync())?.Code);
        }

        var captured = backend.CapturedText.ToArray();
        Assert.NotEmpty(captured);
        Assert.Contains(captured, line => line.Contains("failed closed", StringComparison.Ordinal));

        var leaks = LeakDetector.Find(captured, backend.Tokens.AllTokens);

        Assert.True(leaks.Count == 0, $"{leaks.Count} captured line(s) contained a token. First: {Truncate(leaks.FirstOrDefault())}");
    }

    [Fact]
    public async Task PositiveControl_DetectorCatchesATokenInALogLine()
    {
        await using var backend = await FakeBackend.StartAsync();
        var token = backend.Tokens.Mint(TestPerson.UserA, "Graph");

        backend.Logs.Add($"retrieval failed for Authorization: Bearer {token}");
        backend.Logs.Add($"exception: {new InvalidOperationException("boom " + token)}");

        var byShape = LeakDetector.Find(backend.CapturedText, []);
        var byValue = LeakDetector.Find(["opaque-" + token.Replace('.', '~')], [token.Replace('.', '~')]);

        Assert.Equal(2, byShape.Count);
        Assert.Single(byValue);
    }

    [Fact]
    public async Task PositiveControl_ExceptionsFromRealFailuresAreCaptured()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.Expired);

        var reply = await backend.TurnAsync(TestPerson.UserA, "expired");

        // The handler logged the failure, so the scan above has real failure output to inspect.
        Assert.True(reply.SignInRequired);
        Assert.Contains(backend.Logs.Lines, l => l.Contains(nameof(DownstreamAuthenticationException), StringComparison.Ordinal));
    }

    private static string Truncate(string? line) => line is null ? "<none>" : line[..Math.Min(line.Length, 200)];
}
