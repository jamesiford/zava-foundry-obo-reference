using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Host.Turns;

namespace Compass.Bot.Application.Tests;

/// <summary>What the answer cites is what the person is shown as sources: not every extract, and never a source the answer did not use.</summary>
public sealed class BriefingCitationsRetrievalTests
{
    private static readonly RetrievalResult Hits = new(
    [
        new RetrievalHit("https://contoso.sharepoint.com/a.docx", "A", "text a", 0.9),
        new RetrievalHit("https://contoso.sharepoint.com/b.docx", "B", "text b", 0.8),
    ]);

    private static MemberBriefing Briefing(KnowledgeQueryResult? summary, bool unavailable = false) =>
        new(TestPeople.UserA, "answer", null, Hits, null, SharePointRoute.Retrieval, summary, unavailable);

    [Fact]
    public void Without_a_summary_every_extract_is_cited_as_before()
    {
        var citations = BriefingCitations.From(Briefing(null));

        Assert.Equal(["A", "B"], citations.Select(c => c.Title));
    }

    [Fact]
    public void An_unavailable_summary_cites_the_extracts_that_are_shown()
    {
        var citations = BriefingCitations.From(Briefing(null, unavailable: true));

        Assert.Equal(["A", "B"], citations.Select(c => c.Title));
    }

    [Fact]
    public void A_summary_cites_only_what_it_cited()
    {
        var citations = BriefingCitations.From(Briefing(new KnowledgeQueryResult("Answer.", [new KnowledgeSource("B", "https://contoso.sharepoint.com/b.docx")])));

        var citation = Assert.Single(citations);
        Assert.Equal("B", citation.Title);
        Assert.Equal("https://contoso.sharepoint.com/b.docx", citation.Url);
    }

    [Fact]
    public void A_no_answer_summary_cites_nothing()
    {
        var citations = BriefingCitations.From(Briefing(new KnowledgeQueryResult("I don't know", [new KnowledgeSource("A", "https://contoso.sharepoint.com/a.docx")])));

        Assert.Empty(citations);
    }
}
