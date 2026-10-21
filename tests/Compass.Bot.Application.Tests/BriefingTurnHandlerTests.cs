using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Bot.Host.Turns;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Bot.Application.Tests;

public sealed class BriefingTurnHandlerTests
{
    private static BriefingTurnHandler Create(BotTestHost host, out IServiceScope scope)
    {
        scope = host.Provider.CreateScope();
        return new BriefingTurnHandler(scope.ServiceProvider.GetRequiredService<ISender>(), NullLogger<BriefingTurnHandler>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_turn_with_no_sso_token_asks_to_sign_in_and_touches_nothing(string? token)
    {
        var host = new BotTestHost();
        var handler = Create(host, out var scope);
        using var _ = scope;

        var reply = await handler.HandleAsync(token, "hello", CancellationToken.None);

        Assert.True(reply.SignInRequired);
        Assert.Equal(BriefingTurnHandler.SignInMessage, reply.Text);
        Assert.Equal(0, host.Person.Calls);
        Assert.Empty(host.Broker.Calls);
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task A_good_turn_replies_with_the_composed_briefing()
    {
        var host = new BotTestHost();
        var handler = Create(host, out var scope);
        using var _ = scope;

        var reply = await handler.HandleAsync(FakeTokens.Create("sso"), "my accounts", CancellationToken.None);

        Assert.False(reply.SignInRequired);
        Assert.Contains("Daniel Park", reply.Text);
    }

    public static TheoryData<Exception> AuthFailures() =>
    [
        new PersonResolutionException("bad token"),
        new TokenAcquisitionException(DownstreamAudience.Graph, "denied"),
        new MissingDownstreamTokenException("x-client-zava-api-token"),
        new DownstreamAuthenticationException("Zava", 401),
        new ClaimsChallengeException("Graph", "{}"),
    ];

    [Theory]
    [MemberData(nameof(AuthFailures))]
    public async Task Every_auth_failure_becomes_a_sign_in_prompt_with_no_answer(Exception failure)
    {
        var host = new BotTestHost(broker: new RecordingBroker { FailWith = failure });
        var handler = Create(host, out var scope);
        using var _ = scope;

        var reply = await handler.HandleAsync(FakeTokens.Create("sso"), "my accounts", CancellationToken.None);

        Assert.True(reply.SignInRequired);
        Assert.Equal(BriefingTurnHandler.SignInMessage, reply.Text);
    }

    [Fact]
    public async Task An_unlinked_person_gets_a_specific_message_not_a_sign_in_loop()
    {
        var host = new BotTestHost(broker: new RecordingBroker { FailWith = new MemberNotLinkedException() });
        var handler = Create(host, out var scope);
        using var _ = scope;

        var reply = await handler.HandleAsync(FakeTokens.Create("sso"), "my accounts", CancellationToken.None);

        Assert.False(reply.SignInRequired);
        Assert.Contains("member record", reply.Text);
    }

    [Fact]
    public async Task An_agent_outage_is_reported_without_a_partial_answer()
    {
        var host = new BotTestHost(gateway: new RecordingGateway { FailWith = new HostedAgentException(HostedAgentName.ZavaApi, 503), FailFor = HostedAgentName.ZavaApi });
        var handler = Create(host, out var scope);
        using var _ = scope;

        var reply = await handler.HandleAsync(FakeTokens.Create("sso"), "my accounts", CancellationToken.None);

        Assert.False(reply.SignInRequired);
        Assert.DoesNotContain("Daniel", reply.Text);
        Assert.Contains("unavailable", reply.Text);
    }

    [Fact]
    public async Task Unexpected_exceptions_are_not_swallowed()
    {
        var host = new BotTestHost(gateway: new RecordingGateway { FailWith = new InvalidOperationException("bug") });
        var handler = Create(host, out var scope);
        using var _ = scope;

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(FakeTokens.Create("sso"), "x", CancellationToken.None));
    }
}
