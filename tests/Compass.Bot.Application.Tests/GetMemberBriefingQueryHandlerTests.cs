using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Queries;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Application.Tests;

/// <summary>Contract rules 1, 2 and 5 at the orchestration level: one person, one exchange per audience, fail closed.</summary>
public sealed class GetMemberBriefingQueryHandlerTests
{
    private static readonly string Sso = FakeTokens.Create("sso");

    private static Task<Models.MemberBriefing> RunAsync(BotTestHost host)
    {
        var scope = host.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(Sso), "loan terms"), CancellationToken.None);
    }

    [Fact]
    public async Task Resolves_the_person_exactly_once_per_turn()
    {
        var host = new BotTestHost();

        await RunAsync(host);

        Assert.Equal(1, host.Person.Calls);
    }

    [Fact]
    public async Task Performs_one_exchange_per_audience_from_the_same_sso_token()
    {
        var host = new BotTestHost();

        await RunAsync(host);

        Assert.Equal(3, host.Broker.Calls.Count);
        Assert.Single(host.Broker.Calls, c => c.Audience == DownstreamAudience.Graph);
        Assert.Single(host.Broker.Calls, c => c.Audience == DownstreamAudience.Search);
        Assert.Single(host.Broker.Calls, c => c.Audience == DownstreamAudience.ZavaApi);
        Assert.All(host.Broker.Calls, c => Assert.Equal(Sso, c.Assertion.Reveal()));
    }

    [Fact]
    public async Task Forwards_each_downstream_token_only_to_its_own_agent_header()
    {
        var host = new BotTestHost();

        await RunAsync(host);

        var graph = host.Gateway.Calls.Single(c => c.Agent == Application.Abstractions.HostedAgentName.SharePointRetrieval);
        var knowledge = host.Gateway.Calls.Single(c => c.Agent == Application.Abstractions.HostedAgentName.SharePointKnowledge);
        var zava = host.Gateway.Calls.Single(c => c.Agent == Application.Abstractions.HostedAgentName.ZavaApi);

        Assert.Equal([CompassHeaders.GraphAccessToken], graph.ForwardedTokens.Keys);
        Assert.Equal(RecordingBroker.TokenFor(DownstreamAudience.Graph), graph.ForwardedTokens[CompassHeaders.GraphAccessToken].Reveal());
        Assert.Equal([CompassHeaders.SearchToken], knowledge.ForwardedTokens.Keys);
        Assert.Equal(RecordingBroker.TokenFor(DownstreamAudience.Search), knowledge.ForwardedTokens[CompassHeaders.SearchToken].Reveal());
        Assert.Equal([CompassHeaders.ZavaApiToken], zava.ForwardedTokens.Keys);
        Assert.Equal(RecordingBroker.TokenFor(DownstreamAudience.ZavaApi), zava.ForwardedTokens[CompassHeaders.ZavaApiToken].Reveal());
    }

    [Fact]
    public async Task All_agent_calls_carry_the_same_person_and_session()
    {
        var host = new BotTestHost();

        await RunAsync(host);

        Assert.Equal(3, host.Gateway.Calls.Count);
        Assert.All(host.Gateway.Calls, c => Assert.Equal(TestPeople.UserA, c.Person));
        Assert.Single(host.Gateway.Calls.Select(c => c.Session).Distinct());
    }

    [Fact]
    public async Task Composes_one_answer_from_all_three_agents()
    {
        var host = new BotTestHost();

        var briefing = await RunAsync(host);

        Assert.Contains("Daniel Park", briefing.Answer);
        Assert.Contains("1,250.50", briefing.Answer);
        Assert.Contains("Loan note", briefing.Answer);
        Assert.Contains("Daniel's knowledge answer", briefing.Answer);
        Assert.Contains("Handbook.docx", briefing.Answer);
    }

    [Theory]
    [InlineData(DownstreamAudience.Graph)]
    [InlineData(DownstreamAudience.Search)]
    [InlineData(DownstreamAudience.ZavaApi)]
    public async Task Fails_closed_when_an_exchange_fails_and_calls_no_agent(DownstreamAudience failing)
    {
        var host = new BotTestHost(broker: new RecordingBroker { FailWith = new TokenAcquisitionException(failing, "denied"), FailFor = failing });

        await Assert.ThrowsAsync<TokenAcquisitionException>(() => RunAsync(host));

        Assert.Empty(host.Gateway.Calls);
    }

    [Theory]
    [InlineData(Application.Abstractions.HostedAgentName.SharePointRetrieval)]
    [InlineData(Application.Abstractions.HostedAgentName.SharePointKnowledge)]
    [InlineData(Application.Abstractions.HostedAgentName.ZavaApi)]
    public async Task Fails_closed_when_any_agent_rejects_the_token_with_no_partial_answer(Application.Abstractions.HostedAgentName failing)
    {
        var host = new BotTestHost(gateway: new RecordingGateway { FailWith = new DownstreamAuthenticationException(failing.ToString(), 401), FailFor = failing });

        await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => RunAsync(host));
    }

    [Fact]
    public async Task Surfaces_a_claims_challenge_so_the_host_can_trigger_step_up()
    {
        var host = new BotTestHost(broker: new RecordingBroker
        {
            FailWith = new TokenAcquisitionException(DownstreamAudience.Graph, "step-up", claims: "{\"access_token\":{}}"),
        });

        var ex = await Assert.ThrowsAsync<TokenAcquisitionException>(() => RunAsync(host));

        Assert.Equal("{\"access_token\":{}}", ex.Claims);
    }

    [Fact]
    public async Task A_successful_turn_never_writes_a_token_to_the_logs()
    {
        var host = new BotTestHost();

        await RunAsync(host);

        var all = string.Join('\n', host.Logs.Lines);
        Assert.DoesNotContain(Sso, all);
        Assert.DoesNotContain(RecordingBroker.TokenFor(DownstreamAudience.Graph), all);
        Assert.DoesNotContain(RecordingBroker.TokenFor(DownstreamAudience.Search), all);
        Assert.DoesNotContain(RecordingBroker.TokenFor(DownstreamAudience.ZavaApi), all);
    }

    [Fact]
    public void The_query_has_no_person_member_or_session_parameters_so_none_can_come_from_the_caller()
    {
        var names = typeof(GetMemberBriefingQuery).GetConstructors().Single().GetParameters().Select(p => p.Name!).ToArray();

        // Progress is an output observer, not an input about who is asking, so it does not weaken the rule.
        Assert.Equal(["SsoToken", "Question", "Progress"], names);
    }
}
