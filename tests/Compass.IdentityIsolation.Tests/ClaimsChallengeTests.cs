using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.Bot.Host.Turns;
using Compass.IdentityIsolation.Tests.Support;
using Compass.Shared.Identity.Tokens;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// Case 6 (dev spec §12): a Conditional Access claims challenge must travel unchanged from the downstream, through
/// the agent and the gateway, to the bot — where it becomes a sign-in prompt rather than a swallowed error.
/// </summary>
public sealed class ClaimsChallengeTests
{
    [Fact]
    public async Task ChallengeFromGraph_ReachesTheBotWithItsClaimsIntact()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);

        var ex = await Assert.ThrowsAsync<ClaimsChallengeException>(() => backend.BriefAsync(TestPerson.UserA, "hello"));

        Assert.Equal(TokenDirectory.ClaimsChallenge, ex.Claims);
    }

    [Fact]
    public async Task ChallengeThroughTheGateway_PreservesClaims()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);
        var person = new PersonContext(TestPerson.UserA.ObjectId, TestPerson.UserA.TenantId, TestPerson.UserA.DisplayName);
        var graphToken = backend.Tokens.Mint(person: TestPerson.UserA, audience: "Graph");
        var call = new HostedAgentCall(
            HostedAgentName.SharePointRetrieval,
            person,
            SessionId.Mint(person),
            new Dictionary<string, AccessToken> { [CompassHeaders.GraphAccessToken] = new(graphToken) });

        var ex = await Assert.ThrowsAsync<ClaimsChallengeException>(
            () => backend.Gateway.InvokeAsync<RetrievalRequest, RetrievalResult>(call, new RetrievalRequest("policies"), CancellationToken.None));

        Assert.Equal(TokenDirectory.ClaimsChallenge, ex.Claims);
    }

    [Fact]
    public async Task ChallengeFromGraph_IsShownAsSignInRequired_NotSwallowed()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.ClaimsChallenge);

        var reply = await backend.TurnAsync(TestPerson.UserA, "hello");

        Assert.True(reply.SignInRequired);
        Assert.Equal(BriefingTurnHandler.SignInMessage, reply.Text);
    }
}
