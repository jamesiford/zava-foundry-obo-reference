using Compass.Bot.Domain;

namespace Compass.Bot.Application.Models;

/// <summary>
/// Everything one turn's fan-out needs, already decided: who the person is, the session minted for them, the tokens
/// exchanged for them, the question, and the branches in scope for it. The orchestrator only runs the agents; it never resolves or exchanges identity,
/// so identity decisions stay in one place (the handler) whichever orchestrator is plugged in.
/// </summary>
public sealed record BriefingPlan(
    PersonContext Person,
    SessionId Session,
    DownstreamTokenSet Tokens,
    string Question,
    string? MemberNumber,
    BriefingScope Scope);
