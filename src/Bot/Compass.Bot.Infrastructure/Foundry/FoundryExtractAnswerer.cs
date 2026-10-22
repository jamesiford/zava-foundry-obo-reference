using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Infrastructure.Foundry;

/// <summary>
/// Writes the route A answer by sending the question and this turn's extracts to the SharePoint answer prompt agent. The
/// prompt and the reply mapping are the pure <see cref="ExtractAnswerPrompt"/>; this only moves them across the wire.
/// </summary>
/// <remarks>
/// Authenticated as the bot's own identity like every Foundry call. There is no person here: the call carries no token, id,
/// session or header for one, and the agent has no tools, so the extracts are the only thing it can answer from.
/// </remarks>
public sealed class FoundryExtractAnswerer(IAnswerAgent agent) : IAnswerFromExtracts
{
    public async Task<KnowledgeQueryResult> AnswerAsync(string question, IReadOnlyList<RetrievalHit> extracts, CancellationToken cancellationToken)
    {
        var reply = await agent.AnswerAsync(ExtractAnswerPrompt.Build(question, extracts), cancellationToken);
        return ExtractAnswerPrompt.ToResult(reply, extracts);
    }
}
