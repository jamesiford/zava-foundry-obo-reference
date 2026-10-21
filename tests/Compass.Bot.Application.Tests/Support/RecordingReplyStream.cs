using System.Diagnostics;
using Compass.Bot.Host.Turns;
using Microsoft.Agents.Builder;

namespace Compass.Bot.Application.Tests.Support;

/// <summary>
/// A scriptable stand-in for the channel's stream. It records the order of every call and fails the test's invariants
/// (overlapping calls, updates after the end) instead of throwing, so assertions can name exactly what went wrong.
/// </summary>
internal sealed class RecordingReplyStream : IReplyStream, IReplyStreamFactory
{
    private readonly object _lock = new();
    private int _inFlight;

    public List<string> Events { get; } = [];

    public List<(string Text, long Ticks)> Updates { get; } = [];

    public List<(string Text, IReadOnlyList<ReplyCitation> Citations)> Ends { get; } = [];

    public int MaxInFlight { get; private set; }

    public int UpdatesAfterEnd { get; private set; }

    public int Created { get; private set; }

    /// <summary>Delay inside every update, to give an overlapping caller time to show up.</summary>
    public TimeSpan UpdateDelay { get; init; } = TimeSpan.Zero;

    /// <summary>Makes every update throw, as a channel that refuses streaming would.</summary>
    public Exception? RefuseUpdates { get; init; }

    /// <summary>With <see cref="RefuseUpdates"/>: whether the channel had already opened the stream when it refused.</summary>
    public bool OpenedBeforeRefusing { get; init; }

    public Exception? FailEnd { get; init; }

    public StreamEndResult EndResult { get; init; } = StreamEndResult.Delivered;

    public bool IsStarted { get; private set; }

    public IReplyStream Create(ITurnContext turnContext)
    {
        Created++;
        return this;
    }

    public async Task InformAsync(string text, CancellationToken cancellationToken)
    {
        Enter();
        try
        {
            lock (_lock)
            {
                Events.Add($"inform:{text}");
                if (Ends.Count > 0)
                {
                    UpdatesAfterEnd++;
                }
            }

            if (UpdateDelay > TimeSpan.Zero)
            {
                await Task.Delay(UpdateDelay, cancellationToken);
            }

            if (RefuseUpdates is not null)
            {
                IsStarted = OpenedBeforeRefusing;
                throw RefuseUpdates;
            }

            lock (_lock)
            {
                Updates.Add((text, Stopwatch.GetTimestamp()));
            }

            IsStarted = true;
        }
        finally
        {
            Leave();
        }
    }

    public Task<StreamEndResult> EndAsync(string text, IReadOnlyList<ReplyCitation> citations, CancellationToken cancellationToken)
    {
        Enter();
        try
        {
            lock (_lock)
            {
                Events.Add($"end:{text}");
                Ends.Add((text, citations));
            }

            if (FailEnd is not null)
            {
                throw FailEnd;
            }

            return Task.FromResult(EndResult);
        }
        finally
        {
            Leave();
        }
    }

    private void Enter()
    {
        lock (_lock)
        {
            _inFlight++;
            MaxInFlight = Math.Max(MaxInFlight, _inFlight);
        }
    }

    private void Leave()
    {
        lock (_lock)
        {
            _inFlight--;
        }
    }
}
