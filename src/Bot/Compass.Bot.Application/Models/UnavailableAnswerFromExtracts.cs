using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Abstractions;

namespace Compass.Bot.Application.Models;

/// <summary>
/// The answerer used when nothing real is registered. It always fails, which the retrieval answer step turns into "show
/// the extracts", so a deployment without an answer agent degrades to the plain extracts rather than to an error.
/// </summary>
internal sealed class UnavailableAnswerFromExtracts : IAnswerFromExtracts
{
    public Task<KnowledgeQueryResult> AnswerAsync(string question, IReadOnlyList<RetrievalHit> extracts, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No answer agent is registered.");
}
