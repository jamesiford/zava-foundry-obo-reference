using Compass.Bot.Application.Models;
using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// M11d: choosing a SharePoint route removes branches and tokens, never identity checks. Under each route both people,
/// in both orders, still see only their own data, and a route that is off is never exchanged for or called.
/// </summary>
public sealed class SharePointRouteIsolationTests
{
    private const string Question = "What should I know about my account and our policies?";

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase)]
    [InlineData(SharePointRoute.Retrieval)]
    [InlineData(SharePointRoute.Both)]
    public async Task EachPersonSeesOnlyTheirOwnDataUnderEveryRoute_InBothOrders(SharePointRoute route)
    {
        await using var backend = await FakeBackend.StartAsync(route);

        foreach (var (first, second) in new[] { (TestPerson.UserA, TestPerson.UserB), (TestPerson.UserB, TestPerson.UserA) })
        {
            foreach (var person in new[] { first, second, first })
            {
                var briefing = await backend.BriefAsync(person, Question);

                Assert.Equal(person.MemberNumber, briefing.Accounts.MemberNumber);
                Assert.Contains(person.DisplayName, briefing.Answer);
                Assert.DoesNotContain(person.ForeignName, briefing.Answer);

                if (briefing.Documents is { } documents)
                {
                    var text = string.Join('\n', documents.Hits.Select(h => h.Text));
                    Assert.Contains(person.Canary, text);
                    Assert.DoesNotContain(person.ForeignCanary, text);
                }

                if (briefing.Knowledge is { } knowledge)
                {
                    Assert.Contains(person.Canary, knowledge.Answer);
                    Assert.DoesNotContain(person.ForeignCanary, knowledge.Answer);
                }
            }
        }
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, 0, 1, 1)]
    [InlineData(SharePointRoute.Retrieval, 1, 0, 1)]
    [InlineData(SharePointRoute.Both, 1, 1, 1)]
    public async Task OnlyTheRoutesChosenAreExchangedForAndCalled(SharePointRoute route, int graph, int knowledge, int zavaApi)
    {
        await using var backend = await FakeBackend.StartAsync(route);

        await backend.BriefAsync(TestPerson.UserA, Question);

        Assert.Equal(graph, backend.Graph.Calls.Count);
        Assert.Equal(knowledge, backend.Knowledge.Calls.Count);
        Assert.Equal(zavaApi, backend.ZavaApi.Calls.Count);
        Assert.Equal(graph + knowledge + zavaApi, backend.Broker.Exchanges.Count);
        Assert.Equal(graph, backend.Broker.Exchanges.Count(e => e.Audience == Compass.Bot.Domain.DownstreamAudience.Graph));
        Assert.Equal(knowledge, backend.Broker.Exchanges.Count(e => e.Audience == Compass.Bot.Domain.DownstreamAudience.Search));
    }
}
