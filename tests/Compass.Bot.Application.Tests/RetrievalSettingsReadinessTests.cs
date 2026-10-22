using Compass.Bot.Application.Models;
using Compass.Bot.Host.Readiness;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Application.Tests;

/// <summary>The route A settings are validated by name only: a bad value is reported by key, and the value is never echoed.</summary>
public sealed class RetrievalSettingsReadinessTests
{
    private static IConfiguration Build(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void Nothing_configured_means_no_cut_and_summarising_on()
    {
        var settings = RetrievalSettingsReader.TryRead(Build());

        Assert.NotNull(settings);
        Assert.Equal(0, settings.MinRelevance);
        Assert.True(settings.Summarise);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.72", 0.72)]
    [InlineData(" 1 ", 1)]
    [InlineData("0.5", 0.5)]
    public void A_valid_minimum_is_read_with_the_invariant_culture(string raw, double expected)
    {
        var settings = RetrievalSettingsReader.TryRead(Build((RetrievalSettings.MinRelevanceKey, raw)));

        Assert.Equal(expected, settings!.MinRelevance);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-0.1")]
    [InlineData("1.01")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0,7")]
    public void An_invalid_minimum_is_rejected(string raw) =>
        Assert.Null(RetrievalSettingsReader.TryRead(Build((RetrievalSettings.MinRelevanceKey, raw))));

    [Fact]
    public void An_invalid_summarise_value_is_rejected_and_a_valid_one_is_read()
    {
        Assert.Null(RetrievalSettingsReader.TryRead(Build((RetrievalSettings.SummariseKey, "maybe"))));
        Assert.False(RetrievalSettingsReader.TryRead(Build((RetrievalSettings.SummariseKey, "false")))!.Summarise);
    }

    [Fact]
    public void Startup_fails_fast_on_an_invalid_value_without_echoing_it()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RetrievalSettingsReader.Read(Build((RetrievalSettings.MinRelevanceKey, "SECRET-LOOKING-VALUE"))));

        Assert.DoesNotContain("SECRET-LOOKING-VALUE", ex.Message, StringComparison.Ordinal);
        Assert.Contains(RetrievalSettings.MinRelevanceKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_names_a_bad_minimum_and_never_its_value()
    {
        var values = ReadinessConfiguration();
        values[RetrievalSettings.MinRelevanceKey] = "SECRET-LOOKING-VALUE";

        var missing = BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        var label = Assert.Single(missing);
        Assert.StartsWith(RetrievalSettings.MinRelevanceKey, label, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-LOOKING-VALUE", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_names_a_bad_summarise_switch()
    {
        var values = ReadinessConfiguration();
        values[RetrievalSettings.SummariseKey] = "maybe";

        var label = Assert.Single(BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));

        Assert.StartsWith(RetrievalSettings.SummariseKey, label, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_requires_the_answer_agent_name_only_when_it_will_be_used()
    {
        var retrieval = ReadinessConfiguration();
        retrieval["Bot:SharePoint:Route"] = "Retrieval";
        retrieval["Bot:Foundry:AnswerAgent:AgentName"] = "<answer-agent>";
        var knowledgeBase = ReadinessConfiguration();
        knowledgeBase["Bot:SharePoint:Route"] = "KnowledgeBase";
        knowledgeBase["Bot:Foundry:AnswerAgent:AgentName"] = "<answer-agent>";
        var off = ReadinessConfiguration();
        off["Bot:SharePoint:Route"] = "Retrieval";
        off[RetrievalSettings.SummariseKey] = "false";
        off["Bot:Foundry:AnswerAgent:AgentName"] = "<answer-agent>";

        Assert.Equal(["Bot:Foundry:AnswerAgent:AgentName"], BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(retrieval).Build()));
        Assert.Empty(BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(knowledgeBase).Build()));
        Assert.Empty(BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(off).Build()));
    }

    [Fact]
    public void The_default_complete_configuration_reports_nothing_with_the_new_settings_present()
    {
        var values = ReadinessConfiguration();
        values["Bot:SharePoint:Route"] = "Both";
        values[RetrievalSettings.MinRelevanceKey] = "0.7";
        values[RetrievalSettings.SummariseKey] = "true";
        values["Bot:Foundry:AnswerAgent:AgentName"] = "SharePointAnswerAgent";

        Assert.Empty(BotConfigurationReadiness.FindMissing(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    private static Dictionary<string, string?> ReadinessConfiguration() => new()
    {
        ["Connections:ServiceConnection:Settings:ClientId"] = "bot-client",
        ["Connections:ServiceConnection:Settings:FederatedClientId"] = "mi-client",
        ["Connections:ServiceConnection:Settings:TenantId"] = "tenant",
        ["Bot:Channel:ValidAudiences:0"] = "bot-client",
        ["Bot:Auth:Authority"] = "https://login.example/tenant/v2.0",
        ["Bot:Auth:ValidIssuers:0"] = "https://login.example/tenant/v2.0",
        ["Bot:Auth:ValidAudiences:0"] = "api://botid-bot-client",
        ["Bot:Obo:TenantId"] = "tenant",
        ["Bot:Obo:ClientId"] = "bot-client",
        ["Bot:Obo:ManagedIdentityClientId"] = "mi-client",
        ["Bot:Obo:ZavaApiScopes:0"] = "api://zava/.default",
        ["Bot:Obo:SearchScopes:0"] = "https://search.azure.com/.default",
        ["Bot:Foundry:ProjectEndpoint"] = "https://foundry.example/api/projects/p",
        ["Bot:Foundry:SharePointRetrievalAgent:Name"] = "SharePointRetrievalAgent",
        ["Bot:Foundry:ZavaApiAgent:Name"] = "ZavaApiAgent",
        ["Bot:Foundry:KnowledgeAgent:AgentName"] = "SharePointKnowledgeAgent",
        ["Bot:Foundry:KnowledgeAgent:ModelDeployment"] = "model",
        ["Bot:Foundry:KnowledgeAgent:ProjectConnectionName"] = "kb-connection",
        ["Bot:Foundry:KnowledgeAgent:KnowledgeBaseMcpEndpoint"] = "https://search.example/knowledgebases/kb/mcp",
        ["Bot:Foundry:AnswerAgent:AgentName"] = "SharePointAnswerAgent",
        ["AgentApplication:UserAuthorization:DefaultHandlerName"] = "teamsSso",
        ["AgentApplication:UserAuthorization:Handlers:teamsSso:Settings:AzureBotOAuthConnectionName"] = "teams-sso",
        ["Bot:Members:ByObjectId:oid-1"] = "ZCU-100234",
    };
}
