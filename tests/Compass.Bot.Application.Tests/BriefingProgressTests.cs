using System.Collections.Concurrent;
using System.Reflection;
using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// Progress is advisory output that leaves the identity boundary (a chat, a browser), so it must be structurally
/// content-free, correctly ordered, and unable to hurt the turn it describes.
/// </summary>
public sealed class BriefingProgressTests
{
    private static readonly string Sso = FakeTokens.Create("sso");

    private sealed class RecordingSink : IBriefingProgressSink
    {
        private readonly ConcurrentQueue<BriefingProgress> _seen = new();

        public IReadOnlyList<BriefingProgress> Seen => [.. _seen];

        public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken)
        {
            _seen.Enqueue(progress);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IBriefingProgressSink
    {
        public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sink blew up");
    }

    private sealed class StuckSink : IBriefingProgressSink
    {
        public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static Task<MemberBriefing> RunAsync(BotTestHost host, IBriefingProgressSink? sink)
    {
        var scope = host.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(Sso), "loan terms", sink), CancellationToken.None);
    }

    [Fact]
    public void Progress_has_no_member_that_could_carry_free_text()
    {
        var allowed = new[] { typeof(BriefingStage), typeof(BriefingBranch), typeof(BranchOutcome), typeof(SharePointRoute), typeof(long) };

        foreach (var property in typeof(BriefingProgress).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            Assert.Contains(type, allowed);
        }
    }

    [Fact]
    public async Task A_successful_turn_starts_first_completes_branches_in_any_order_then_composes_then_completes()
    {
        var sink = new RecordingSink();

        await RunAsync(new BotTestHost(), sink);
        var seen = sink.Seen;

        Assert.Equal(BriefingStage.Started, seen[0].Stage);
        Assert.Equal(BriefingStage.Completed, seen[^1].Stage);

        var branches = Enum.GetValues<BriefingBranch>();
        foreach (var branch in branches)
        {
            Assert.Single(seen, p => p.Stage == BriefingStage.BranchStarted && p.Branch == branch);
            Assert.Single(seen, p => p.Stage == BriefingStage.BranchCompleted && p.Branch == branch);
        }

        var composing = seen.ToList().FindIndex(p => p.Stage == BriefingStage.Composing);
        var lastBranchCompleted = seen.ToList().FindLastIndex(p => p.Stage == BriefingStage.BranchCompleted);
        Assert.Single(seen, p => p.Stage == BriefingStage.Composing);
        Assert.True(composing > lastBranchCompleted, "Composing must follow every branch completion.");
        Assert.True(seen.Zip(seen.Skip(1)).All(pair => pair.First.ElapsedMilliseconds <= pair.Second.ElapsedMilliseconds));
    }

    [Fact]
    public async Task A_failing_branch_reports_failed_then_rethrows_the_original_exception()
    {
        var failure = new DownstreamAuthenticationException("zava-api", 401);
        var sink = new RecordingSink();
        var host = new BotTestHost(gateway: new RecordingGateway { FailWith = failure, FailFor = HostedAgentName.ZavaApi });

        var thrown = await Assert.ThrowsAsync<DownstreamAuthenticationException>(() => RunAsync(host, sink));

        Assert.Same(failure, thrown);
        var seen = sink.Seen;
        Assert.Single(seen, p => p.Stage == BriefingStage.Failed);
        Assert.Equal(BriefingStage.Failed, seen[^1].Stage);
        Assert.DoesNotContain(seen, p => p.Stage == BriefingStage.Completed);
    }

    [Fact]
    public async Task A_sink_that_throws_does_not_break_the_turn()
    {
        var briefing = await RunAsync(new BotTestHost(), new ThrowingSink());

        Assert.Equal(TestPeople.UserA, briefing.Person);
    }

    [Fact]
    public async Task A_sink_that_never_returns_does_not_hold_the_turn_forever()
    {
        var briefing = await RunAsync(new BotTestHost(), new StuckSink()).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(TestPeople.UserA, briefing.Person);
    }

    [Fact]
    public async Task Not_passing_a_sink_behaves_as_before()
    {
        var briefing = await RunAsync(new BotTestHost(), sink: null);

        Assert.Equal(TestPeople.UserA, briefing.Person);
    }
}
