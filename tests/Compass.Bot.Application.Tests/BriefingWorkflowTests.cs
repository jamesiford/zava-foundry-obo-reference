using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// The MAF workflow must keep what the old <c>Task.WhenAll</c> gave us (concurrency, fail-closed, correct token
/// routing) while adding nothing that can weaken the identity contract.
/// </summary>
public sealed class BriefingWorkflowTests
{
    private static readonly string Sso = FakeTokens.Create("sso");

    private static Task<MemberBriefing> RunAsync(BotTestHost host)
    {
        var scope = host.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(Sso), "loan terms"), CancellationToken.None);
    }

    [Fact]
    public async Task The_three_branches_run_concurrently()
    {
        // Each call waits until all three are in flight; a sequential run would never release them.
        var gated = new GatedGateway(new RecordingGateway(), expectedConcurrentCalls: 3);
        var host = new BotTestHost(gatewayOverride: gated);

        var briefing = await RunAsync(host).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, gated.MaxConcurrent);
        Assert.NotNull(briefing);
    }

    [Fact]
    public async Task The_composed_answer_matches_the_composer_output_for_the_same_branch_results()
    {
        var host = new BotTestHost();

        var briefing = await RunAsync(host);

        Assert.Equal(BriefingComposer.Compose(briefing.Accounts, briefing.Documents, briefing.Knowledge, briefing.Route), briefing.Answer);
        Assert.Equal(TestPeople.UserA, briefing.Person);
    }

    [Theory]
    [InlineData(HostedAgentName.SharePointRetrieval)]
    [InlineData(HostedAgentName.SharePointKnowledge)]
    [InlineData(HostedAgentName.ZavaApi)]
    public async Task Any_failing_branch_fails_the_whole_turn_with_its_original_typed_exception(HostedAgentName failing)
    {
        var failure = new DownstreamAuthenticationException("zava-api", 401);
        var host = new BotTestHost(gateway: new RecordingGateway { FailWith = failure, FailFor = failing });

        var thrown = await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => RunAsync(host));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task A_failed_branch_is_not_masked_by_the_other_branches_finishing()
    {
        // The other two branches complete normally; the turn must still produce no briefing.
        var host = new BotTestHost(gateway: new RecordingGateway
        {
            FailWith = new InvalidOperationException("boom"),
            FailFor = HostedAgentName.SharePointKnowledge,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(host));
    }

    [Fact]
    public async Task A_claims_challenge_from_the_exchange_still_reaches_the_caller_untouched()
    {
        var host = new BotTestHost(broker: new RecordingBroker
        {
            FailWith = new TokenAcquisitionException(DownstreamAudience.Search, "step-up", claims: "{\"access_token\":{}}"),
            FailFor = DownstreamAudience.Search,
        });

        var ex = await Assert.ThrowsAsync<TokenAcquisitionException>(() => RunAsync(host));

        Assert.Equal("{\"access_token\":{}}", ex.Claims);
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task Concurrent_turns_for_different_people_never_share_workflow_state()
    {
        var userA = new BotTestHost(person: new CountingPersonResolver(TestPeople.UserA));
        var userB = new BotTestHost(person: new CountingPersonResolver(TestPeople.UserB));

        var results = await Task.WhenAll(RunAsync(userA), RunAsync(userB), RunAsync(userA), RunAsync(userB));

        Assert.Equal([TestPeople.UserA, TestPeople.UserB, TestPeople.UserA, TestPeople.UserB], results.Select(r => r.Person));
    }
}

/// <summary>Holds every call until the expected number are in flight at once, proving the branches overlap.</summary>
internal sealed class GatedGateway(IHostedAgentGateway inner, int expectedConcurrentCalls) : IHostedAgentGateway
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inFlight;
    private int _max;

    public int MaxConcurrent => _max;

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(HostedAgentCall call, TRequest request, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _inFlight);
        InterlockedMax(ref _max, current);
        if (current >= expectedConcurrentCalls)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Interlocked.Decrement(ref _inFlight);
        return await inner.InvokeAsync<TRequest, TResponse>(call, request, cancellationToken);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
