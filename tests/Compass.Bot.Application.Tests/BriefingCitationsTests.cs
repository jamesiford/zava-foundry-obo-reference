using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Host.Turns;

namespace Compass.Bot.Application.Tests;

public sealed class BriefingCitationsTests
{
    private static MemberBriefing Briefing(params KnowledgeSource[] sources) => new(
        TestPeople.UserA,
        "answer",
        new MemberBriefingResult("ZCU-1", "Daniel Park", "Active", [], 0m),
        null,
        new KnowledgeQueryResult("answer", sources),
        SharePointRoute.KnowledgeBase);

    [Fact]
    public void A_knowledge_source_is_cited_by_its_readable_title_and_links_to_its_document()
    {
        var citations = BriefingCitations.From(Briefing(
            new KnowledgeSource("Member Services FAQ", "https://contoso.sharepoint.com/sites/demo/MemberServicesFAQ/Member%20Services%20FAQ.docx")));

        var citation = Assert.Single(citations);
        Assert.Equal("Member Services FAQ", citation.Title);
        Assert.Equal("https://contoso.sharepoint.com/sites/demo/MemberServicesFAQ/Member%20Services%20FAQ.docx", citation.Url);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/secret.docx")]
    public void A_source_address_that_is_not_a_web_link_is_shown_as_a_title_only(string url)
    {
        var citation = Assert.Single(BriefingCitations.From(Briefing(new KnowledgeSource("Doc", url))));

        Assert.Equal("Doc", citation.Title);
        Assert.Null(citation.Url);
    }

    [Fact]
    public void A_source_without_an_address_is_a_title_only_citation()
    {
        var citation = Assert.Single(BriefingCitations.From(Briefing("Handbook.docx")));

        Assert.Equal("Handbook.docx", citation.Title);
        Assert.Null(citation.Url);
    }
}
