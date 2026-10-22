using Compass.Agents.Contracts.Retrieval;

namespace Compass.Bot.Application.Models;

/// <summary>
/// Drops weak Graph retrieval hits before they are shown or summarised. The one place a relevance cut-off exists.
/// </summary>
/// <remarks>
/// This is a relevance filter, not a permission filter. Which documents a person may read is decided by the store (contract
/// rule 4): the hosted agent returns everything the person's token let the store return and never prunes it. The Retrieval
/// API is optimised for recall, so it also returns weak matches the person is allowed to read; this gate removes those
/// in the bot, where it can be tested without touching the identity path. It can only remove hits from what the store
/// returned, never add one, so it cannot widen what a person sees.
/// </remarks>
public static class RetrievalRelevanceGate
{
    /// <summary>
    /// Keeps hits whose score is at or above <paramref name="minRelevance"/>, in the order the API returned them (the
    /// API's order is "unordered", so nothing here re-ranks). A hit with no score is kept: the API documents the score as
    /// possibly missing, and hiding content because a number is absent would be silent loss, not a relevance decision.
    /// </summary>
    public static RetrievalResult Apply(RetrievalResult documents, double minRelevance)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (minRelevance <= 0)
        {
            return documents;
        }

        var kept = documents.Hits.Where(hit => hit.Relevance is not { } score || score >= minRelevance).ToList();
        return kept.Count == documents.Hits.Count ? documents : new RetrievalResult(kept);
    }
}
