using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Domain;

namespace Compass.Bot.Application.Models;

/// <summary>The composed answer for one turn plus the structured parts it was built from.</summary>
/// <param name="Accounts">The member's accounts; <see langword="null"/> when the question was not about them.</param>
/// <param name="Documents">Graph retrieval result; <see langword="null"/> when the route did not run it.</param>
/// <param name="Knowledge">Knowledge-base result; <see langword="null"/> when the route did not run it.</param>
/// <param name="Route">The SharePoint route this turn used.</param>
/// <param name="RetrievalSummary">The answer agent's reply for the Graph retrieval hits; <see langword="null"/> when not summarised.</param>
/// <param name="SummaryUnavailable">The answer agent was needed but failed, so the retrieval hits were shown instead.</param>
public sealed record MemberBriefing(
    PersonContext Person,
    string Answer,
    MemberBriefingResult? Accounts,
    RetrievalResult? Documents,
    KnowledgeQueryResult? Knowledge,
    SharePointRoute Route,
    KnowledgeQueryResult? RetrievalSummary = null,
    bool SummaryUnavailable = false);
