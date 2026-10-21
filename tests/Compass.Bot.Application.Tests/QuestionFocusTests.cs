using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Tests;

public sealed class QuestionFocusTests
{
    [Theory]
    [InlineData("What are my accounts and balances?", QuestionFocus.Accounts)]
    [InlineData("What is my checking balance?", QuestionFocus.Accounts)]
    [InlineData("How much do I have in savings?", QuestionFocus.Accounts)]
    [InlineData("What is the maximum debt-to-income ratio for an unsecured personal loan?", QuestionFocus.Policy)]
    [InlineData("Which branches are on the delinquency watchlist this quarter?", QuestionFocus.Policy)]
    [InlineData("When must we make a hardship call on a delinquent account?", QuestionFocus.Policy)]
    [InlineData("What is the cutoff time for outgoing domestic wires?", QuestionFocus.Policy)]
    [InlineData("What should I know about my account and our policies?", QuestionFocus.Both)]
    [InlineData("loan terms", QuestionFocus.Both)]
    [InlineData("Tell me about the member", QuestionFocus.Both)]
    [InlineData("", QuestionFocus.Both)]
    [InlineData(null, QuestionFocus.Both)]
    public void The_question_decides_which_parts_of_a_briefing_it_is_about(string? question, QuestionFocus expected) =>
        Assert.Equal(expected, QuestionFocusClassifier.Classify(question));

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, QuestionFocus.Accounts, true, false, false)]
    [InlineData(SharePointRoute.KnowledgeBase, QuestionFocus.Policy, false, false, true)]
    [InlineData(SharePointRoute.KnowledgeBase, QuestionFocus.Both, true, false, true)]
    [InlineData(SharePointRoute.Retrieval, QuestionFocus.Accounts, true, false, false)]
    [InlineData(SharePointRoute.Retrieval, QuestionFocus.Policy, false, true, false)]
    [InlineData(SharePointRoute.Retrieval, QuestionFocus.Both, true, true, false)]
    [InlineData(SharePointRoute.Both, QuestionFocus.Accounts, true, false, false)]
    [InlineData(SharePointRoute.Both, QuestionFocus.Policy, false, true, true)]
    [InlineData(SharePointRoute.Both, QuestionFocus.Both, true, true, true)]
    public void The_scope_is_the_route_narrowed_by_the_focus_and_never_wider(
        SharePointRoute route, QuestionFocus focus, bool accounts, bool retrieval, bool knowledge)
    {
        var scope = new BriefingRoutePolicy(route).ScopeFor(focus);

        Assert.Equal(new BriefingScope(accounts, retrieval, knowledge), scope);
    }

    [Fact]
    public void The_scope_names_only_the_audiences_its_branches_need()
    {
        var scope = new BriefingRoutePolicy(SharePointRoute.Both).ScopeFor(QuestionFocus.Policy);

        Assert.Equal([Compass.Bot.Domain.DownstreamAudience.Graph, Compass.Bot.Domain.DownstreamAudience.Search], scope.Audiences);
    }
}
