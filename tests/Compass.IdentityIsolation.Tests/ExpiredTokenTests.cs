using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Host.Turns;
using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// Case 5 (dev spec §12): a downstream rejecting an expired token is a typed failure, the person is asked to sign in,
/// the rejected token is never retried or replaced by an app-only identity, and the next turn does a fresh exchange.
/// </summary>
public sealed class ExpiredTokenTests
{
    [Theory]
    [InlineData("Graph")]
    [InlineData("ZavaApi")]
    public async Task ExpiredDownstreamToken_FailsClosedWithATypedException(string audience)
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext(audience, TokenState.Expired);

        var ex = await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => backend.BriefAsync(TestPerson.UserA, "hello"));

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task ExpiredDownstreamToken_AsksForSignInAndLeaksNoData()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.Expired);

        var reply = await backend.TurnAsync(TestPerson.UserA, "hello");

        Assert.True(reply.SignInRequired);
        Assert.Equal(BriefingTurnHandler.SignInMessage, reply.Text);
        Assert.DoesNotContain(TestPerson.CanaryA, reply.Text);
    }

    [Fact]
    public async Task ExpiredDownstreamToken_IsNeitherRetriedNorReplacedWithAnotherIdentity()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.Expired);

        await backend.TurnAsync(TestPerson.UserA, "hello");

        // Exactly one attempt, with the person's own (rejected) token: no retry, no app-only fallback.
        var attempt = Assert.Single(backend.Graph.Calls);
        Assert.Equal(TestPerson.UserA.ObjectId, attempt.TokenOwnerObjectId);
    }

    [Fact]
    public async Task NextTurnAfterExpiry_DoesAFreshExchangeAndNeverReusesTheRejectedToken()
    {
        await using var backend = await FakeBackend.StartAsync();
        backend.Tokens.ScriptNext("Graph", TokenState.Expired);

        await backend.TurnAsync(TestPerson.UserA, "first");
        var rejected = Assert.Single(backend.Graph.Calls).Token;
        var exchangesBefore = backend.Broker.Exchanges.Count;

        var second = await backend.TurnAsync(TestPerson.UserA, "second");

        Assert.False(second.SignInRequired);
        Assert.Contains(TestPerson.CanaryA, second.Text);
        Assert.Equal(2, backend.Graph.Calls.Count);
        Assert.NotEqual(rejected, backend.Graph.Calls.Last().Token);
        Assert.True(backend.Broker.Exchanges.Count > exchangesBefore, "The second turn must run its own on-behalf-of exchange.");
        Assert.Equal(backend.Broker.Exchanges.Count, backend.Broker.Exchanges.Select(e => e.Issued).Distinct().Count());
    }
}
