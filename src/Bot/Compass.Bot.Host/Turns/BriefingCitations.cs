using Compass.Bot.Application.Models;

namespace Compass.Bot.Host.Turns;

/// <summary>Turns a briefing's structured parts into the source list every front door shows next to the answer.</summary>
public static class BriefingCitations
{
    /// <summary>
    /// Titles and links only, de-duplicated: the sources are what SharePoint showed *this person*, nothing broader.
    /// </summary>
    public static IReadOnlyList<ReplyCitation> From(MemberBriefing briefing)
    {
        var citations = new List<ReplyCitation>();
        // When the retrieval hits were summarised, cite only what the answer cited (and nothing if it said "I don't know"):
        // listing every extract would show documents the answer does not rest on.
        var retrievalSources = briefing.RetrievalSummary is { } summary
            ? KnowledgeAnswers.HasAnswer(summary) ? summary.Sources.Select(s => (s.Title, s.Url)) : []
            : (briefing.Documents?.Hits ?? []).Select(hit => (hit.Title ?? hit.SiteUrl, (string?)hit.SiteUrl));
        foreach (var (title, url) in retrievalSources)
        {
            citations.Add(new ReplyCitation(title, url is null ? null : AsWebLink(url)));
        }

        foreach (var source in briefing.Knowledge?.Sources ?? [])
        {
            citations.Add(new ReplyCitation(source.Title, source.Url is null ? null : AsWebLink(source.Url)));
        }

        return citations.DistinctBy(c => (c.Title, c.Url)).ToList();
    }

    // Only http(s) is linkable: a source string is data from an index, and must never become a script or file link.
    private static string? AsWebLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;
}
