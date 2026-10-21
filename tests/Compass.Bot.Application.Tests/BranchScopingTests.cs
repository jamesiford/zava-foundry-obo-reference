using Compass.Agents.Contracts;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// A turn runs only the branches its question needs, and never asks for a token it will not use. The route still sets the
/// ceiling: the question can remove branches but never add one the deployment does not allow.
/// </summary>
public sealed class BranchScopingTests
{
    private static readonly string Sso = FakeTokens.Create("sso");

    private const string AccountsQuestion = "What are my accounts and balances?";
    private const string PolicyQuestion = "What is the maximum debt-to-income ratio for an unsecured personal loan?";

    private static Task<MemberBriefing> AskAsync(BotTestHost host, string question)
    {
        var scope = host.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(Sso), question), CancellationToken.None);
    }

    private static string[] Agents(BotTestHost host) => [.. host.Gateway.Calls.Select(c => c.Agent.ToString()).Order()];

    private static string[] Audiences(BotTestHost host) => [.. host.Broker.Calls.Select(c => c.Audience.ToString()).Order()];

    [Fact]
    public async Task An_accounts_question_runs_only_the_accounts_branch_and_asks_for_only_its_token()
    {
        var host = new BotTestHost(route: SharePointRoute.Both);

        var briefing = await AskAsync(host, AccountsQuestion);

        Assert.Equal(["ZavaApi"], Agents(host));
        Assert.Equal(["ZavaApi"], Audiences(host));
        Assert.NotNull(briefing.Accounts);
        Assert.Null(briefing.Documents);
        Assert.Null(briefing.Knowledge);
        Assert.DoesNotContain("SharePoint route", briefing.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_policy_question_runs_only_the_sharepoint_branches_and_never_asks_for_the_zava_token()
    {
        var host = new BotTestHost(route: SharePointRoute.Both);

        var briefing = await AskAsync(host, PolicyQuestion);

        Assert.Equal(["SharePointKnowledge", "SharePointRetrieval"], Agents(host));
        Assert.Equal(["Graph", "Search"], Audiences(host));
        Assert.Null(briefing.Accounts);
        Assert.DoesNotContain("Daniel Park", briefing.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unclear_question_keeps_every_branch_the_route_allows()
    {
        var host = new BotTestHost(route: SharePointRoute.Both);

        var briefing = await AskAsync(host, "loan terms");

        Assert.Equal(["SharePointKnowledge", "SharePointRetrieval", "ZavaApi"], Agents(host));
        Assert.Equal(["Graph", "Search", "ZavaApi"], Audiences(host));
        Assert.NotNull(briefing.Accounts);
    }

    [Fact]
    public async Task The_route_is_a_ceiling_the_question_cannot_raise()
    {
        var host = new BotTestHost(route: SharePointRoute.KnowledgeBase);

        await AskAsync(host, PolicyQuestion);

        Assert.Equal(["SharePointKnowledge"], Agents(host));
        Assert.Equal(["Search"], Audiences(host));
    }

    [Fact]
    public async Task A_person_with_no_member_record_can_still_ask_a_policy_question()
    {
        var host = new BotTestHost(route: SharePointRoute.KnowledgeBase, members: new UnlinkedMemberResolver());

        var briefing = await AskAsync(host, PolicyQuestion);

        Assert.Null(briefing.Accounts);
        Assert.Equal(["SharePointKnowledge"], Agents(host));
    }

    [Fact]
    public async Task The_same_person_is_still_refused_an_accounts_question_before_any_token_is_exchanged()
    {
        var host = new BotTestHost(members: new UnlinkedMemberResolver());

        await Assert.ThrowsAsync<MemberNotLinkedException>(() => AskAsync(host, AccountsQuestion));

        Assert.Empty(host.Broker.Calls);
        Assert.Empty(host.Gateway.Calls);
    }
}
