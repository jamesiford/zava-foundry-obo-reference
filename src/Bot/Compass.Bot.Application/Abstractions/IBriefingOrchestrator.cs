using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Abstractions;

/// <summary>
/// Runs the three agents for one turn and composes their results. It is a port so the application layer does not
/// depend on the orchestration engine (the Microsoft Agent Framework workflow lives in <c>Compass.Bot.Orchestration</c>).
/// </summary>
/// <remarks>
/// Implementations must fail closed: if any agent fails, the original typed exception propagates and no partial
/// briefing is returned (contract rule 5). Progress is advisory and must never change the outcome.
/// </remarks>
public interface IBriefingOrchestrator
{
    /// <param name="plan">Who the turn runs as and what was asked.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <param name="progress">Optional observer of the turn; <see langword="null"/> means nobody is watching.</param>
    Task<MemberBriefing> RunAsync(BriefingPlan plan, CancellationToken cancellationToken, IBriefingProgressSink? progress = null);
}
