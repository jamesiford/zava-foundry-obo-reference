using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Bot.Host.Readiness;
using Compass.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// M11d: the SharePoint route is a composition-time choice, so a route that isn't selected must neither run nor have
/// its downstream token acquired.
/// </summary>
public sealed class SharePointRouteTests
{
    private static readonly string Sso = FakeTokens.Create("sso");

    private sealed class RecordingSink : IBriefingProgressSink
    {
        public List<BriefingProgress> Seen { get; } = [];

        public Task ReportAsync(BriefingProgress progress, CancellationToken cancellationToken)
        {
            lock (Seen)
            {
                Seen.Add(progress);
            }

            return Task.CompletedTask;
        }
    }

    private static async Task<(MemberBriefing Briefing, RecordingSink Progress)> RunAsync(BotTestHost host)
    {
        var sink = new RecordingSink();
        using var scope = host.Provider.CreateScope();
        var briefing = await scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(Sso), "loan terms", sink), CancellationToken.None);
        return (briefing, sink);
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, new[] { DownstreamAudience.Search, DownstreamAudience.ZavaApi })]
    [InlineData(SharePointRoute.Retrieval, new[] { DownstreamAudience.Graph, DownstreamAudience.ZavaApi })]
    [InlineData(SharePointRoute.Both, new[] { DownstreamAudience.Graph, DownstreamAudience.Search, DownstreamAudience.ZavaApi })]
    public async Task Each_route_acquires_only_its_own_audiences(SharePointRoute route, DownstreamAudience[] expected)
    {
        var host = new BotTestHost(route: route);

        await RunAsync(host);

        Assert.Equal(expected.Order(), host.Broker.Calls.Select(c => c.Audience).Order());
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, new[] { HostedAgentName.SharePointKnowledge, HostedAgentName.ZavaApi })]
    [InlineData(SharePointRoute.Retrieval, new[] { HostedAgentName.SharePointRetrieval, HostedAgentName.ZavaApi })]
    [InlineData(SharePointRoute.Both, new[] { HostedAgentName.SharePointRetrieval, HostedAgentName.SharePointKnowledge, HostedAgentName.ZavaApi })]
    public async Task Each_route_calls_only_its_own_agents(SharePointRoute route, HostedAgentName[] expected)
    {
        var host = new BotTestHost(route: route);

        var (briefing, _) = await RunAsync(host);

        Assert.Equal(expected.Order(), host.Gateway.Calls.Select(c => c.Agent).Order());
        Assert.Equal(route, briefing.Route);
        Assert.Equal(route != SharePointRoute.KnowledgeBase, briefing.Documents is not null);
        Assert.Equal(route != SharePointRoute.Retrieval, briefing.Knowledge is not null);
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, "SharePoint route: knowledge base")]
    [InlineData(SharePointRoute.Retrieval, "SharePoint route: Graph retrieval")]
    [InlineData(SharePointRoute.Both, "SharePoint route: Graph retrieval and knowledge base")]
    public async Task The_composed_answer_names_the_route_used(SharePointRoute route, string expected)
    {
        var (briefing, _) = await RunAsync(new BotTestHost(route: route));

        Assert.EndsWith(expected, briefing.Answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase, BriefingBranch.SharePointRetrieval)]
    [InlineData(SharePointRoute.Retrieval, BriefingBranch.SharePointKnowledge)]
    public async Task Progress_names_the_route_and_never_reports_a_branch_that_was_not_built(SharePointRoute route, BriefingBranch absent)
    {
        var (_, progress) = await RunAsync(new BotTestHost(route: route));

        Assert.All(progress.Seen, p => Assert.Equal(route, p.Route));
        Assert.DoesNotContain(progress.Seen, p => p.Branch == absent);
        Assert.Equal(BriefingStage.Completed, progress.Seen[^1].Stage);
    }

    [Theory]
    [InlineData(SharePointRoute.KnowledgeBase)]
    [InlineData(SharePointRoute.Retrieval)]
    [InlineData(SharePointRoute.Both)]
    public async Task Concurrent_turns_for_two_people_stay_isolated_under_every_route(SharePointRoute route)
    {
        var daniel = new BotTestHost(person: new CountingPersonResolver(TestPeople.UserA), route: route);
        var maya = new BotTestHost(person: new CountingPersonResolver(TestPeople.UserB), route: route);

        var results = await Task.WhenAll(RunAsync(daniel), RunAsync(maya), RunAsync(maya), RunAsync(daniel));

        Assert.Equal([TestPeople.UserA, TestPeople.UserB, TestPeople.UserB, TestPeople.UserA], results.Select(r => r.Briefing.Person));
        Assert.All(daniel.Gateway.Calls, c => Assert.Equal(TestPeople.UserA, c.Person));
        Assert.All(maya.Gateway.Calls, c => Assert.Equal(TestPeople.UserB, c.Person));
    }

    [Theory]
    [InlineData(null, SharePointRoute.KnowledgeBase)]
    [InlineData("", SharePointRoute.KnowledgeBase)]
    [InlineData("knowledgebase", SharePointRoute.KnowledgeBase)]
    [InlineData("Retrieval", SharePointRoute.Retrieval)]
    [InlineData(" BOTH ", SharePointRoute.Both)]
    public void Valid_or_blank_route_settings_parse(string? value, SharePointRoute expected)
    {
        Assert.True(BriefingRoutePolicy.TryParse(value, out var route));
        Assert.Equal(expected, route);
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("Both,Retrieval")]
    [InlineData("KnowledgeBase,Retrieval")]
    [InlineData("Retrieval,Retrieval")]
    [InlineData("Retrieval Both")]
    [InlineData("Retrieval,")]
    [InlineData("-1")]
    public void Unknown_route_settings_are_rejected(string value) =>
        Assert.False(BriefingRoutePolicy.TryParse(value, out _));

    [Fact]
    public void An_unknown_route_fails_fast_at_startup_and_is_reported_by_readiness()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SharePointRouteSetting.Key] = "Everything" })
            .Build();

        var thrown = Assert.Throws<InvalidOperationException>(() => SharePointRouteSetting.Read(configuration));

        Assert.Contains("must be KnowledgeBase, Retrieval or Both", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(SharePointRouteSetting.InvalidLabel, BotConfigurationReadiness.FindMissing(configuration));
        Assert.DoesNotContain("Everything", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_route_defaults_to_the_knowledge_base()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Equal(SharePointRoute.KnowledgeBase, SharePointRouteSetting.Read(configuration));
        Assert.DoesNotContain(SharePointRouteSetting.InvalidLabel, BotConfigurationReadiness.FindMissing(configuration));
    }
}
