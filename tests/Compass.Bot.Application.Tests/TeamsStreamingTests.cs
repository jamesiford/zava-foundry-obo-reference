using System.Text.RegularExpressions;
using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Bot.Host.Turns;
using Compass.Shared.Messaging;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// Teams streaming (M11b). The first group runs the real Agents SDK <c>StreamingResponse</c> against a recording adapter
/// to prove what reaches the wire; the second drives the ordering logic with a scripted stream for the cases the SDK
/// cannot be made to produce (a refused stream, a failed end).
/// </summary>
public sealed partial class TeamsStreamingTests
{
    private static readonly string SsoToken = FakeTokens.Create("sso-streaming");

    private static readonly StreamingOptions FastOptions = new() { MinUpdateInterval = TimeSpan.FromMilliseconds(40) };

    private sealed record Run(RecordingChannelAdapter Adapter, IReadOnlyList<IActivity> Sent);

    private static async Task<Run> RunAsync(
        string conversationType,
        IReplyStreamFactory? streams = null,
        BotTestHost? host = null,
        StreamingOptions? options = null,
        string? displayName = "Daniel Park",
        string? token = null)
    {
        host ??= new BotTestHost();
        options ??= FastOptions;
        using var scope = host.Provider.CreateScope();
        var handler = new BriefingTurnHandler(scope.ServiceProvider.GetRequiredService<ISender>(), NullLogger<BriefingTurnHandler>.Instance);
        var responder = new TeamsTurnResponder(handler, streams ?? new SdkReplyStreamFactory(options), options, TimeProvider.System, NullLogger<TeamsTurnResponder>.Instance);

        var adapter = RecordingChannelAdapter.ForTurn(conversationType, displayName: displayName);
        if (conversationType == "personal")
        {
            // The SDK sends queued chunks on a timer; shorten it so the tests do not wait a second per chunk.
            adapter.Turn.StreamingResponse.Interval = 20;
        }

        await responder.RespondAsync(adapter.Turn, token ?? SsoToken, CancellationToken.None);
        return new Run(adapter, [.. adapter.Sent]);
    }

    private static StreamInfo? StreamOf(IActivity activity) => activity.Entities?.OfType<StreamInfo>().SingleOrDefault();

    private static bool IsFinalStreamMessage(IActivity activity) =>
        activity.Type == ActivityTypes.Message && StreamOf(activity)?.StreamType == StreamTypes.Final;

    // ---- real SDK: what reaches the wire ----------------------------------------------------------------------

    [Fact]
    public async Task A_one_to_one_turn_uses_exactly_one_stream_and_one_final_message()
    {
        var run = await RunAsync("personal");

        // The channel assigns the stream id on the first update, so that one activity legitimately has none yet.
        var streamIds = run.Sent.Select(a => StreamOf(a)?.StreamId).Where(id => id is not null).Distinct().ToList();
        Assert.All(run.Sent, a => Assert.NotNull(StreamOf(a)));
        Assert.Single(streamIds);
        Assert.Single(run.Sent, IsFinalStreamMessage);
    }

    [Fact]
    public async Task The_final_message_and_its_citations_arrive_inside_the_stream_not_as_a_separate_activity()
    {
        var run = await RunAsync("personal");

        var final = Assert.Single(run.Sent, IsFinalStreamMessage);
        Assert.Contains("Daniel Park", final.Text);
        Assert.Contains("[1]", final.Text);

        // The citation entity rides on the stream's own final message...
        Assert.Contains(final.Entities, e => e.Type == "https://schema.org/Message");

        // ...and nothing outside the stream carries an answer or a citation.
        Assert.DoesNotContain(run.Sent, a => StreamOf(a) is null);
    }

    [Fact]
    public async Task Nothing_is_sent_after_the_stream_ends()
    {
        var run = await RunAsync("personal");

        Assert.True(IsFinalStreamMessage(run.Sent[^1]));
        Assert.Equal(1, run.Sent.Count(IsFinalStreamMessage));
    }

    [Fact]
    public async Task Informative_updates_come_first_and_the_answer_last()
    {
        var run = await RunAsync("personal");

        var informative = run.Sent.Take(run.Sent.Count - 2).Select(a => a.Text).ToList();
        Assert.NotEmpty(informative);
        Assert.All(informative, text => Assert.Matches("^(Reading SharePoint as Daniel Park…|Checking your member record…|Composing…)$", text));
        Assert.Equal("Composing…", informative[^1]);
    }

    [Fact]
    public async Task Informative_updates_carry_no_token_and_no_retrieved_text()
    {
        var run = await RunAsync("personal", token: FakeTokens.Create("secret-sso"));

        var informative = run.Sent.Take(run.Sent.Count - 2).ToList();
        Assert.All(informative, a =>
        {
            Assert.DoesNotMatch(JwtShape(), a.Text);
            Assert.DoesNotContain("secret-sso", a.Text);
            Assert.DoesNotContain("loan note", a.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ZCU-", a.Text);
        });
    }

    [Fact]
    public async Task Concurrent_progress_events_never_overlap_sends_on_the_wire()
    {
        var adapter = RecordingChannelAdapter.ForTurn("personal");
        adapter.Turn.StreamingResponse.Interval = 10;
        var stream = new SdkReplyStream(adapter.Turn.StreamingResponse, FastOptions);
        var sink = new StreamingProgressSink(stream, "Daniel Park", FastOptions, TimeProvider.System, NullLogger.Instance);

        var events = new[]
        {
            new BriefingProgress(BriefingStage.BranchStarted, BriefingBranch.SharePointRetrieval, null, 0, SharePointRoute.Both),
            new BriefingProgress(BriefingStage.BranchStarted, BriefingBranch.ZavaApi, null, 1, SharePointRoute.Both),
            new BriefingProgress(BriefingStage.Composing, null, null, 2, SharePointRoute.Both),
        };
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() => sink.ReportAsync(events[i % events.Length], CancellationToken.None))));
        await sink.CloseAsync();
        await stream.EndAsync("done", [], CancellationToken.None);

        Assert.Equal(1, adapter.MaxConcurrentSends);
        Assert.True(IsFinalStreamMessage(adapter.Sent[^1]));
        Assert.Single(adapter.Sent, IsFinalStreamMessage);
    }

    [Fact]
    public async Task A_group_chat_gets_one_interim_message_a_typing_indicator_then_the_answer_as_a_reply()
    {
        var run = await RunAsync("groupChat");

        Assert.Equal(3, run.Sent.Count);
        Assert.Equal(ActivityTypes.Message, run.Sent[0].Type);
        Assert.Equal(TeamsTurnResponder.WorkingMessage, run.Sent[0].Text);
        Assert.Equal(ActivityTypes.Typing, run.Sent[1].Type);
        Assert.Equal(ActivityTypes.Message, run.Sent[2].Type);
        Assert.Contains("Daniel Park", run.Sent[2].Text);
        Assert.All(run.Sent, a => Assert.Null(StreamOf(a)));
        Assert.All(run.Sent, a => Assert.Equal("incoming-1", a.ReplyToId));
    }

    [Fact]
    public async Task A_channel_conversation_is_never_streamed()
    {
        var run = await RunAsync("channel");

        Assert.All(run.Sent, a => Assert.Null(StreamOf(a)));
        Assert.Equal(ActivityTypes.Message, run.Sent[^1].Type);
    }

    // ---- scripted stream: ordering rules the SDK cannot be forced to break ---------------------------------------

    [Fact]
    public async Task The_stream_is_created_once_per_turn_and_ended_once_with_the_answer_and_citations()
    {
        var stream = new RecordingReplyStream();

        var run = await RunAsync("personal", stream);

        Assert.Equal(1, stream.Created);
        var end = Assert.Single(stream.Ends);
        Assert.Contains("Daniel Park", end.Text);
        Assert.Contains(end.Citations, c => c.Url == "https://contoso.sharepoint.com/sites/usera");
        Assert.Empty(run.Sent);
    }

    [Fact]
    public async Task Updates_and_the_end_never_overlap_and_no_update_follows_the_end()
    {
        // A slow channel: the turn finishes while an update is still in flight, which is when a careless sender would end
        // the stream underneath it.
        var stream = new RecordingReplyStream { UpdateDelay = TimeSpan.FromMilliseconds(80) };

        await RunAsync("personal", stream);

        Assert.Equal(1, stream.MaxInFlight);
        Assert.Equal(0, stream.UpdatesAfterEnd);
        Assert.StartsWith("end:", stream.Events[^1]);
    }

    [Fact]
    public async Task Updates_are_rate_limited_and_the_newest_one_wins()
    {
        var options = new StreamingOptions { MinUpdateInterval = TimeSpan.FromMilliseconds(150) };
        var stream = new RecordingReplyStream();
        var gateway = new RecordingGateway { Before = (_, ct) => Task.Delay(300, ct) };

        await RunAsync("personal", stream, new BotTestHost(gateway: gateway), options);

        Assert.True(stream.Updates.Count >= 2);
        for (var i = 1; i < stream.Updates.Count; i++)
        {
            var gap = TimeSpan.FromTicks(stream.Updates[i].Ticks - stream.Updates[i - 1].Ticks);
            Assert.True(gap >= TimeSpan.FromMilliseconds(130), $"update {i} followed the previous by {gap.TotalMilliseconds:F0} ms");
        }

        Assert.Equal("Composing…", stream.Updates[^1].Text);
    }

    [Fact]
    public async Task A_refused_stream_falls_back_to_one_plain_reply_and_never_starts_another()
    {
        var stream = new RecordingReplyStream { RefuseUpdates = new InvalidOperationException("streaming not allowed") };

        var run = await RunAsync("personal", stream);

        Assert.Equal(1, stream.Created);
        Assert.Empty(stream.Ends);
        var reply = Assert.Single(run.Sent);
        Assert.Equal(ActivityTypes.Message, reply.Type);
        Assert.Contains("Daniel Park", reply.Text);
        Assert.Equal("incoming-1", reply.ReplyToId);
    }

    [Fact]
    public async Task A_stream_that_breaks_after_opening_is_still_ended_with_the_answer_before_anything_else()
    {
        var stream = new RecordingReplyStream { RefuseUpdates = new InvalidOperationException("boom"), OpenedBeforeRefusing = true };

        var run = await RunAsync("personal", stream);

        Assert.Single(stream.Ends);
        Assert.Empty(run.Sent);
    }

    [Fact]
    public async Task A_follow_up_after_a_failed_end_is_the_only_message_after_the_stream_and_carries_reply_to_id()
    {
        var stream = new RecordingReplyStream { FailEnd = new InvalidOperationException("channel closed the stream") };

        var run = await RunAsync("personal", stream);

        Assert.Single(stream.Ends);
        var followUp = Assert.Single(run.Sent);
        Assert.Equal("incoming-1", followUp.ReplyToId);
        Assert.Contains("Daniel Park", followUp.Text);
    }

    [Fact]
    public async Task A_follow_up_after_an_undelivered_final_message_carries_reply_to_id()
    {
        var stream = new RecordingReplyStream { EndResult = StreamEndResult.NotDelivered };

        var run = await RunAsync("personal", stream);

        var followUp = Assert.Single(run.Sent);
        Assert.Equal("incoming-1", followUp.ReplyToId);
    }

    [Fact]
    public async Task Nothing_is_sent_after_the_stream_when_the_person_pressed_stop()
    {
        var stream = new RecordingReplyStream { EndResult = StreamEndResult.UserCancelled };

        var run = await RunAsync("personal", stream);

        Assert.Single(stream.Ends);
        Assert.Empty(run.Sent);
    }

    [Fact]
    public async Task A_failure_after_the_stream_opened_still_finalises_the_stream_before_the_error_propagates()
    {
        var stream = new RecordingReplyStream();
        var gateway = new RecordingGateway { FailWith = new InvalidOperationException("bug"), FailFor = HostedAgentName.ZavaApi };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("personal", stream, new BotTestHost(gateway: gateway)));

        var end = Assert.Single(stream.Ends);
        Assert.Equal(TeamsTurnResponder.FailedMessage, end.Text);
        Assert.Equal(0, stream.UpdatesAfterEnd);
    }

    [Fact]
    public async Task A_turn_over_its_deadline_ends_the_stream_with_a_timeout_message()
    {
        var options = new StreamingOptions { MinUpdateInterval = TimeSpan.FromMilliseconds(20), TurnDeadline = TimeSpan.FromMilliseconds(300) };
        var stream = new RecordingReplyStream();
        var gateway = new RecordingGateway { Before = (_, ct) => Task.Delay(Timeout.Infinite, ct) };

        await RunAsync("personal", stream, new BotTestHost(gateway: gateway), options);

        var end = Assert.Single(stream.Ends);
        Assert.Equal(TeamsTurnResponder.TimedOutMessage, end.Text);
    }

    [Fact]
    public async Task A_turn_with_no_token_never_opens_a_stream()
    {
        var stream = new RecordingReplyStream();

        var run = await RunAsync("personal", stream, token: "");

        Assert.Empty(stream.Events);
        var reply = Assert.Single(run.Sent);
        Assert.Equal(BriefingTurnHandler.SignInMessage, reply.Text);
    }

    [Fact]
    public async Task Informative_text_is_capped_and_the_display_name_cannot_inject_control_characters()
    {
        var options = new StreamingOptions { MinUpdateInterval = TimeSpan.FromMilliseconds(10), MaxInformativeLength = 30 };
        var stream = new RecordingReplyStream();

        await RunAsync("personal", stream, options: options, displayName: "Daniel\r\nPark " + new string('x', 500));

        Assert.NotEmpty(stream.Updates);
        Assert.All(stream.Updates, u =>
        {
            Assert.True(u.Text.Length <= 30);
            Assert.DoesNotContain('\n', u.Text);
        });
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.")]
    private static partial Regex JwtShape();
}
