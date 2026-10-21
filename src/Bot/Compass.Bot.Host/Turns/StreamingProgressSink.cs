using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Host.Turns;

/// <summary>
/// Shows briefing progress as Teams informative updates: at most one per interval, newest wins, one send at a time.
/// </summary>
/// <remarks>
/// Progress is advisory, so a refused or failed update switches the sink off rather than failing the turn. After
/// <see cref="CloseAsync"/> returns nothing more is sent, which is what lets the caller end the stream safely.
/// </remarks>
public sealed class StreamingProgressSink(
    IReplyStream stream,
    string? displayName,
    StreamingOptions options,
    TimeProvider time,
    ILogger logger) : IBriefingProgressSink, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _closed = new();
    private readonly long _startedAt = time.GetTimestamp();
    private long _version;
    private long? _lastSentAt;
    private readonly HashSet<string> _sent = [];
    private bool _refused;

    /// <summary>True once the channel rejected an update; the turn then ends with a single plain reply.</summary>
    public bool Refused => Volatile.Read(ref _refused);

    public async Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken)
    {
        var text = ProgressText.For(progress, displayName);
        if (text is null || _closed.IsCancellationRequested)
        {
            return;
        }

        text = Cap(text);

        // A line already shown is not shown again (both SharePoint branches say the same thing), and it must not
        // supersede a different line still waiting its turn.
        if (HasShown(text))
        {
            return;
        }
        // Each text-bearing event supersedes the ones before it: while one waits for its turn, a newer one replaces it.
        var mine = Interlocked.Increment(ref _version);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        try
        {
            await _gate.WaitAsync(linked.Token);
            try
            {
                if (Refused || mine != Volatile.Read(ref _version) || HasShown(text))
                {
                    return;
                }

                if (_lastSentAt is { } last)
                {
                    var wait = options.MinUpdateInterval - time.GetElapsedTime(last);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, time, linked.Token);
                        if (mine != Volatile.Read(ref _version))
                        {
                            return;
                        }
                    }
                }

                // Past the deadline the turn is about to be abandoned; an update would only race the final message.
                if (time.GetElapsedTime(_startedAt) >= options.TurnDeadline)
                {
                    return;
                }

                await stream.InformAsync(text, cancellationToken);
                lock (_sent)
                {
                    _sent.Add(text);
                }
                _lastSentAt = time.GetTimestamp();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Volatile.Write(ref _refused, true);
                logger.LogWarning("Streaming update was refused ({ExceptionType}); falling back to a single reply", ex.GetType().Name);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Closed or cancelled while waiting: advisory, so just stop.
        }
    }

    /// <summary>Stops all further updates and waits for one in flight to finish.</summary>
    public async Task CloseAsync()
    {
        await _closed.CancelAsync();
        await _gate.WaitAsync();
        _gate.Release();
    }

    // Not disposing the gate or token source: a late progress event may still be unwinding, and neither holds unmanaged resources.
    public async ValueTask DisposeAsync() => await CloseAsync();

    private bool HasShown(string text)
    {
        lock (_sent)
        {
            return _sent.Contains(text);
        }
    }

    private string Cap(string text) =>
        text.Length <= options.MaxInformativeLength ? text : string.Concat(text.AsSpan(0, options.MaxInformativeLength - 1), "…");
}
