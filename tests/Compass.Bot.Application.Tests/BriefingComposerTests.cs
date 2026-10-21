using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Tests;

/// <summary>The composer shows exactly what ran: a branch that was not in scope arrives as null and adds nothing.</summary>
public sealed class BriefingComposerTests
{
    private static readonly MemberBriefingResult Accounts = new(
        "ZCU-100234",
        "Daniel Park",
        "Active",
        [new AccountBriefing("1001", "Checking", "Everyday", 1250.50m, "USD")],
        1250.50m);

    private static readonly KnowledgeQueryResult Grounded = new(
        "The maximum DTI is **43%**.",
        [new KnowledgeSource("Consumer Lending Policy", "https://contoso.sharepoint.com/sites/demo/LendingPolicy/Consumer%20Lending%20Policy.docx")]);

    private static readonly KnowledgeQueryResult DontKnow = new("I don't know", []);

    [Fact]
    public void Accounts_alone_show_no_document_section_and_no_route_line()
    {
        var text = BriefingComposer.Compose(Accounts, null, null, SharePointRoute.KnowledgeBase);

        Assert.Contains("Daniel Park", text, StringComparison.Ordinal);
        Assert.Contains("Net balance: 1,250.50", text, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledge base", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No document you have access to", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SharePoint route", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grounded_answer_alone_shows_no_account_block()
    {
        var text = BriefingComposer.Compose(null, null, Grounded, SharePointRoute.KnowledgeBase);

        Assert.Contains("**43%**", text, StringComparison.Ordinal);
        Assert.Contains("Sources: Consumer Lending Policy", text, StringComparison.Ordinal);
        Assert.Contains("SharePoint route: knowledge base", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Daniel Park", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Net balance", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_visible_says_so_once_instead_of_showing_an_empty_section()
    {
        var text = BriefingComposer.Compose(null, null, DontKnow, SharePointRoute.KnowledgeBase);

        Assert.Contains("No document you have access to answers this question.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("I don't know", text, StringComparison.Ordinal);
        Assert.DoesNotContain("From the knowledge base", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I don't know")]
    [InlineData("I don’t know.")]
    [InlineData("  i do not know  ")]
    [InlineData("")]
    public void A_blank_or_dont_know_knowledge_answer_never_appears_next_to_the_accounts(string answer)
    {
        var text = BriefingComposer.Compose(Accounts, null, new KnowledgeQueryResult(answer, []), SharePointRoute.KnowledgeBase);

        Assert.Contains("Daniel Park", text, StringComparison.Ordinal);
        Assert.DoesNotContain("From the knowledge base", text, StringComparison.Ordinal);
        Assert.Contains("No document you have access to answers this question.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_parts_are_shown_when_both_ran_and_have_content()
    {
        var text = BriefingComposer.Compose(Accounts, null, Grounded, SharePointRoute.KnowledgeBase);

        Assert.Contains("Daniel Park", text, StringComparison.Ordinal);
        Assert.Contains("From the knowledge base:", text, StringComparison.Ordinal);
        Assert.Contains("SharePoint route: knowledge base", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Retrieval_hits_are_listed_and_an_empty_retrieval_is_dropped_next_to_a_grounded_answer()
    {
        var withHits = BriefingComposer.Compose(
            null,
            new RetrievalResult([new RetrievalHit("https://contoso.sharepoint.com/x.docx", "Policy", "Text", 0.9)]),
            null,
            SharePointRoute.Retrieval);
        var empty = BriefingComposer.Compose(null, new RetrievalResult([]), Grounded, SharePointRoute.Both);

        Assert.Contains("From SharePoint:", withHits, StringComparison.Ordinal);
        Assert.DoesNotContain("From SharePoint:", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("No document you have access to", empty, StringComparison.Ordinal);
        Assert.Contains("**43%**", empty, StringComparison.Ordinal);
    }
}
