using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;

namespace Compass.Bot.Application.Abstractions;

/// <summary>
/// Answers a question from extracts the caller already fetched for one person. It never receives, and so cannot assert,
/// who the person is: the only inputs are the question and this turn's extracts.
/// </summary>
/// <remarks>
/// An answer of "I don't know" (or an empty one) means the extracts do not answer the question; callers treat it as
/// <see cref="Models.KnowledgeAnswers.HasAnswer"/> does for the knowledge-base route.
/// </remarks>
public interface IAnswerFromExtracts
{
    Task<KnowledgeQueryResult> AnswerAsync(string question, IReadOnlyList<RetrievalHit> extracts, CancellationToken cancellationToken);
}
