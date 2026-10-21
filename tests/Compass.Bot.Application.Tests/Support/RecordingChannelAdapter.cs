using System.Security.Claims;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Bot.Application.Tests.Support;

/// <summary>
/// A channel adapter that records every outgoing activity in order and notices overlapping sends, so tests can check what
/// the real Agents SDK <c>StreamingResponse</c> puts on the wire without a Teams connection.
/// </summary>
public sealed class RecordingChannelAdapter() : ChannelAdapter(NullLogger.Instance)
{
    private readonly object _lock = new();
    private int _inFlight;

    public List<IActivity> Sent { get; } = [];

    /// <summary>Highest number of sends that were in flight at the same moment.</summary>
    public int MaxConcurrentSends { get; private set; }

    public override async Task<ResourceResponse[]> SendActivitiesAsync(ITurnContext turnContext, IActivity[] activities, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _inFlight++;
            MaxConcurrentSends = Math.Max(MaxConcurrentSends, _inFlight);
        }

        // Yield so an overlapping caller has a real chance to enter before this send completes.
        await Task.Delay(5, cancellationToken);

        lock (_lock)
        {
            Sent.AddRange(activities);
            _inFlight--;
        }

        return [.. activities.Select(a => new ResourceResponse(a.Id ?? Guid.NewGuid().ToString("N")))];
    }

    /// <summary>Builds a Teams turn on a fresh adapter; read what was sent from <see cref="Sent"/>.</summary>
    public static RecordingChannelAdapter ForTurn(string conversationType, string text = "hi", string? displayName = "Daniel Park")
    {
        var adapter = new RecordingChannelAdapter();
        var activity = new Activity
        {
            Type = ActivityTypes.Message,
            Id = "incoming-1",
            Text = text,
            ChannelId = Channels.Msteams,
            ServiceUrl = "https://smba.example.invalid/",
            Conversation = new ConversationAccount { Id = "conversation-1", ConversationType = conversationType },
            From = new ChannelAccount { Id = "user-1", Name = displayName },
            Recipient = new ChannelAccount { Id = "bot-1", Name = "Compass" },
        };
        adapter.Turn = new TurnContext(adapter, activity, new ClaimsIdentity());
        return adapter;
    }

    public TurnContext Turn { get; private set; } = null!;
}
