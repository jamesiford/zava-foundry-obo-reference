using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// Fail closed on content, fail soft on availability. The relevance gate can only remove hits. If the answer agent errors
/// or runs out of time, the caller gets the same gated extracts the person is allowed to read, flagged so the composer can
/// say so, and never anything broader.
/// </summary>
public sealed class PrepareRetrievalAnswerQueryHandler(RetrievalSettings settings, IAnswerFromExtracts answerer)
    : IRequestHandler<PrepareRetrievalAnswerQuery, RetrievalAnswer>
{
    public async Task<RetrievalAnswer> HandleAsync(PrepareRetrievalAnswerQuery request, CancellationToken cancellationToken)
    {
        var gated = RetrievalRelevanceGate.Apply(request.Documents, settings.MinRelevance);

        // Nothing left means no model call at all: there is nothing to summarise and the composer shows the no-document line.
        if (gated.Hits.Count == 0 || !settings.Summarise)
        {
            return new RetrievalAnswer(gated, null, false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            var summary = await answerer.AnswerAsync(request.Question, gated.Hits, timeout.Token);
            return summary is null ? new RetrievalAnswer(gated, null, true) : new RetrievalAnswer(gated, summary, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new RetrievalAnswer(gated, null, true);
        }
    }
}
