using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Queries;

namespace Compass.Bot.Orchestration;

/// <summary>
/// What one parallel branch of the workflow produced. It is a single sealed type because the workflow routes messages
/// by exact runtime type; a base class with per-branch subclasses would never reach the fan-in handler.
/// </summary>
internal sealed record BranchResult
{
    public RetrievalResult? Documents { get; private init; }

    public KnowledgeQueryResult? Knowledge { get; private init; }

    /// <summary>The answer agent's reply for the retrieval branch; <see langword="null"/> when the extracts are shown as they are.</summary>
    public KnowledgeQueryResult? RetrievalSummary { get; private init; }

    public bool SummaryUnavailable { get; private init; }

    public MemberBriefingResult? Accounts { get; private init; }

    public static BranchResult FromDocuments(RetrievalResult documents) => new() { Documents = documents };

    public static BranchResult FromRetrievalAnswer(RetrievalAnswer answer) =>
        new() { Documents = answer.Documents, RetrievalSummary = answer.Summary, SummaryUnavailable = answer.SummaryUnavailable };

    public static BranchResult FromKnowledge(KnowledgeQueryResult knowledge) => new() { Knowledge = knowledge };

    public static BranchResult FromAccounts(MemberBriefingResult accounts) => new() { Accounts = accounts };
}
