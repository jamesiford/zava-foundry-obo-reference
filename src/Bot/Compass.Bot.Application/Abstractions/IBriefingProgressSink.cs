using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Abstractions;

/// <summary>
/// Receives the advisory <see cref="BriefingProgress"/> of one turn. Progress never influences the result: an
/// orchestrator must not wait on a slow sink and must ignore a sink that throws.
/// </summary>
public interface IBriefingProgressSink
{
    Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken);
}

/// <summary>The default sink: callers that do not show progress pay nothing for it.</summary>
public sealed class NullBriefingProgressSink : IBriefingProgressSink
{
    public static readonly NullBriefingProgressSink Instance = new();

    private NullBriefingProgressSink()
    {
    }

    public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;
}
