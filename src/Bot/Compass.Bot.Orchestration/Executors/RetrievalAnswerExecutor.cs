using Compass.Bot.Application.Queries;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration.Executors;

/// <summary>
/// Branch 1, second step: applies the relevance gate to what Graph retrieval returned and, if anything survives, has the
/// answer agent write the answer from those extracts. It forwards only this turn's own branch result, so the extracts
/// never leave the person's turn.
/// </summary>
/// <remarks>
/// It is a separate executor so "Writing the answer" is a visible step in the progress stream. It sends a query that
/// carries the question and the hits and nothing about the person.
/// </remarks>
internal sealed class RetrievalAnswerExecutor(ISender sender, string question) : Executor<BranchResult, BranchResult>(BriefingExecutorIds.RetrievalAnswer)
{
    public override async ValueTask<BranchResult> HandleAsync(BranchResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var documents = message.Documents ?? throw new InvalidOperationException("The retrieval step produced no result.");
        var answer = await sender.SendAsync(new PrepareRetrievalAnswerQuery(question, documents), cancellationToken);
        return BranchResult.FromRetrievalAnswer(answer);
    }
}
