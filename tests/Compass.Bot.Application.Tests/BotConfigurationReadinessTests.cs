using Compass.Bot.Host.Readiness;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Application.Tests;

public sealed class BotConfigurationReadinessTests
{
    private static Dictionary<string, string?> Complete() => new()
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
        ["AgentApplication:UserAuthorization:DefaultHandlerName"] = "teamsSso",
        ["AgentApplication:UserAuthorization:Handlers:teamsSso:Settings:AzureBotOAuthConnectionName"] = "teams-sso",
        ["Bot:Members:ByObjectId:oid-1"] = "ZCU-100234",
    };

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Complete_configuration_reports_nothing_missing() =>
        Assert.Empty(BotConfigurationReadiness.FindMissing(Build(Complete())));

    [Fact]
    public void Unfilled_template_placeholders_count_as_missing()
    {
        var values = Complete();
        values["Bot:Obo:TenantId"] = "<tenant-id>";

        Assert.Equal(["Bot:Obo:TenantId"], BotConfigurationReadiness.FindMissing(Build(values)));
    }

    [Fact]
    public void Absent_member_mapping_is_reported()
    {
        var values = Complete();
        values.Remove("Bot:Members:ByObjectId:oid-1");

        var missing = Assert.Single(BotConfigurationReadiness.FindMissing(Build(values)));
        Assert.StartsWith("Bot:Members:ByObjectId", missing);
    }

    [Fact]
    public void Authority_endpoint_can_stand_in_for_the_tenant_id()
    {
        var values = Complete();
        values.Remove("Connections:ServiceConnection:Settings:TenantId");
        values["Connections:ServiceConnection:Settings:AuthorityEndpoint"] = "https://login.example/tenant";

        Assert.Empty(BotConfigurationReadiness.FindMissing(Build(values)));
    }

    [Fact]
    public void A_placeholder_authority_endpoint_is_reported_even_when_the_tenant_id_is_set()
    {
        var values = Complete();
        values["Connections:ServiceConnection:Settings:AuthorityEndpoint"] = "https://login.microsoftonline.com/<tenant-id>";

        var missing = Assert.Single(BotConfigurationReadiness.FindMissing(Build(values)));
        Assert.StartsWith("Connections:ServiceConnection:Settings:AuthorityEndpoint", missing);
    }

    [Fact]
    public void The_shipped_appsettings_keeps_no_authority_placeholder_that_a_real_tenant_id_cannot_override()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var shipped = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var filled = shipped.AsEnumerable().Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value);
        filled["Connections:ServiceConnection:Settings:TenantId"] = "tenant";

        Assert.DoesNotContain(BotConfigurationReadiness.FindMissing(Build(filled)), m => m.StartsWith("Connections:ServiceConnection:Settings:Authority", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_names_only_never_configured_values()
    {
        var values = Complete();
        values["Bot:Obo:ClientId"] = "";
        values["Bot:Auth:Authority"] = "https://secret-looking.example";

        var missing = BotConfigurationReadiness.FindMissing(Build(values));

        Assert.Equal(["Bot:Obo:ClientId"], missing);
        Assert.DoesNotContain(missing, m => m.Contains("secret-looking", StringComparison.Ordinal));
    }
}
