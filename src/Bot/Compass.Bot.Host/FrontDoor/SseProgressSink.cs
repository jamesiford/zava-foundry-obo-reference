using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>The wire shape of one progress event. Enums and numbers only, mirroring <see cref="BriefingProgress"/>.</summary>
public sealed record ProgressEvent(
    BriefingStage Stage,
    BriefingBranch? Branch,
    BranchOutcome? Outcome,
    long ElapsedMilliseconds,
    SharePointRoute Route)
{
    public static ProgressEvent From(BriefingProgress progress) =>
        new(progress.Stage, progress.Branch, progress.Outcome, progress.ElapsedMilliseconds, progress.Route);
}

/// <summary>Streams each <see cref="BriefingProgress"/> to the browser as a <c>progress</c> event.</summary>
public sealed class SseProgressSink(SseResponseWriter writer) : IBriefingProgressSink
{
    public const string EventName = "progress";

    public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken) =>
        writer.WriteAsync(EventName, ProgressEvent.From(progress), cancellationToken);
}
