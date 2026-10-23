using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Tests;

/// <summary>Composer behaviour for the summarised route A section. The legacy composer cases stay in <see cref="BriefingComposerTests"/>.</summary>
public sealed class BriefingComposerRetrievalSummaryTests
{
    private const string NoDocument = "No document you have access to answers this question.";

    private static readonly RetrievalResult Hits = new([new RetrievalHit("https://contoso.sharepoint.com/x.docx", "Policy", "Raw extract paragraph.", 0.9)]);

    private static readonly KnowledgeQueryResult Summary = new("The cap is **43%**.", [new KnowledgeSource("Policy", "https://contoso.sharepoint.com/x.docx")]);

    private static readonly KnowledgeQueryResult Grounded = new("KB answer.", [new KnowledgeSource("Handbook", null)]);

    [Fact]
    public void A_summary_replaces_the_raw_extracts_and_lists_its_sources()
    {
        var text = BriefingComposer.Compose(null, Hits, null, SharePointRoute.Retrieval, Summary);

        Assert.Contains("From SharePoint:", text, StringComparison.Ordinal);
        Assert.Contains("The cap is **43%**.", text, StringComparison.Ordinal);
        Assert.Contains("Sources: Policy", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw extract paragraph.", text, StringComparison.Ordinal);
        Assert.Contains("SharePoint route: Graph retrieval", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I don't know")]
    [InlineData("")]
    public void A_no_answer_summary_shows_the_no_document_line_and_not_the_extracts(string answer)
    {
        var text = BriefingComposer.Compose(null, Hits, null, SharePointRoute.Retrieval, new KnowledgeQueryResult(answer, []));

        Assert.Contains(NoDocument, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw extract paragraph.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("I don't know", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_summary_shows_the_extracts_under_a_note()
    {
        var text = BriefingComposer.Compose(null, Hits, null, SharePointRoute.Retrieval, retrievalSummary: null, summaryUnavailable: true);

        Assert.Contains("From SharePoint:", text, StringComparison.Ordinal);
        Assert.Contains(BriefingComposer.SummaryUnavailableNote, text, StringComparison.Ordinal);
        Assert.Contains("Raw extract paragraph.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_note_is_not_shown_when_the_summary_worked()
    {
        var text = BriefingComposer.Compose(null, Hits, null, SharePointRoute.Retrieval, Summary);

        Assert.DoesNotContain(BriefingComposer.SummaryUnavailableNote, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_left_after_the_gate_shows_the_no_document_line_even_if_a_flag_is_stale()
    {
        var text = BriefingComposer.Compose(null, new RetrievalResult([]), null, SharePointRoute.Retrieval);

        Assert.Contains(NoDocument, text, StringComparison.Ordinal);
        Assert.DoesNotContain("From SharePoint", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_route_both_the_two_sections_are_named_apart()
    {
        var text = BriefingComposer.Compose(null, Hits, Grounded, SharePointRoute.Both, Summary);

        Assert.Contains("From SharePoint (Graph retrieval):", text, StringComparison.Ordinal);
        Assert.Contains("From the knowledge base:", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Graph retrieval):", StringComparison.Ordinal) < text.IndexOf("From the knowledge base:", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_route_both_an_ungrounded_retrieval_does_not_hide_the_knowledge_answer()
    {
        var text = BriefingComposer.Compose(null, Hits, Grounded, SharePointRoute.Both, new KnowledgeQueryResult("I don't know", []));

        Assert.DoesNotContain("From SharePoint", text, StringComparison.Ordinal);
        Assert.Contains("KB answer.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(NoDocument, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_knowledge_base_output_is_the_same_with_or_without_the_new_arguments()
    {
        var legacy = BriefingComposer.Compose(null, null, Grounded, SharePointRoute.KnowledgeBase);
        var explicitDefaults = BriefingComposer.Compose(null, null, Grounded, SharePointRoute.KnowledgeBase, null, false);

        Assert.Equal(legacy, explicitDefaults);
    }
}
