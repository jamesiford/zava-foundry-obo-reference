using System.Threading.Channels;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Orchestration;

/// <summary>
/// Delivers progress to a sink in order without ever making the turn wait on it for longer than a short, bounded drain.
/// </summary>
/// <remarks>
/// Progress is advisory (it must never change a result), so a slow sink is decoupled by a queue, a throwing sink is
/// swallowed, and a stuck sink is abandoned after <paramref name="drainTimeout"/> rather than holding the person's answer hostage.
/// </remarks>
internal sealed class BriefingProgressPump : IAsyncDisposable
{
    private readonly Channel<BriefingProgress> _queue = Channel.CreateUnbounded<BriefingProgress>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _abandon = new();
    private readonly IBriefingProgressSink _sink;
    private readonly TimeSpan _drainTimeout;
    private readonly Task _delivery;

    public BriefingProgressPump(IBriefingProgressSink sink, TimeSpan drainTimeout)
    {
        _sink = sink;
        _drainTimeout = drainTimeout;
        _delivery = Task.Run(DeliverAsync);
    }

    public void Enqueue(BriefingProgress progress) => _queue.Writer.TryWrite(progress);

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        var finished = await Task.WhenAny(_delivery, Task.Delay(_drainTimeout));
        if (finished != _delivery)
        {
            await _abandon.CancelAsync();
        }

        _abandon.Dispose();
    }

    private async Task DeliverAsync()
    {
        try
        {
            await foreach (var progress in _queue.Reader.ReadAllAsync(_abandon.Token))
            {
                try
                {
                    await _sink.ReportAsync(progress, _abandon.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_abandon.IsCancellationRequested)
                {
                    // Advisory by contract: a faulty observer must not affect the turn.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Abandoned after the drain timeout.
        }
    }
}
