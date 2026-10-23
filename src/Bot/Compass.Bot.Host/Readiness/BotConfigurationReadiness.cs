using Compass.Bot.Application.Models;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Host.Readiness;

/// <summary>
/// Reports which configuration the bot still lacks, by name only. A freshly deployed bot starts fine with the template
/// placeholders from <c>appsettings.json</c> and then fails on the first Teams message with an opaque error; this
/// turns that into a checklist the operator can read before sideloading anything. Values are never returned, so the
/// endpoint cannot leak tenant ids, client ids or tokens.
/// </summary>
public static class BotConfigurationReadiness
{
    private const string FederatedConnection = "Connections:ServiceConnection:Settings";

    private static readonly string[] RequiredKeys =
    [
        $"{FederatedConnection}:ClientId",
        $"{FederatedConnection}:FederatedClientId",
        "Bot:Channel:ValidAudiences:0",
        "Bot:Auth:Authority",
        "Bot:Auth:ValidIssuers:0",
        "Bot:Auth:ValidAudiences:0",
        "Bot:Obo:TenantId",
        "Bot:Obo:ClientId",
        "Bot:Obo:ManagedIdentityClientId",
        "Bot:Obo:ZavaApiScopes:0",
        "Bot:Obo:SearchScopes:0",
        "Bot:Foundry:ProjectEndpoint",
        "Bot:Foundry:SharePointRetrievalAgent:Name",
        "Bot:Foundry:ZavaApiAgent:Name",
        "Bot:Foundry:KnowledgeAgent:AgentName",
        "Bot:Foundry:KnowledgeAgent:ModelDeployment",
        "Bot:Foundry:KnowledgeAgent:ProjectConnectionName",
        "Bot:Foundry:KnowledgeAgent:KnowledgeBaseMcpEndpoint",
        "AgentApplication:UserAuthorization:DefaultHandlerName",
        "AgentApplication:UserAuthorization:Handlers:teamsSso:Settings:AzureBotOAuthConnectionName",
    ];

    /// <summary>Returns the names of every missing or still-placeholder setting; empty means the bot is configured.</summary>
    public static IReadOnlyList<string> FindMissing(IConfiguration configuration)
    {
        var missing = RequiredKeys.Where(key => IsMissing(configuration[key])).ToList();

        // The Agents SDK accepts the authority either as a full URL or derived from the tenant id.
        var authorityEndpoint = configuration[$"{FederatedConnection}:AuthorityEndpoint"];
        if (IsMissing(authorityEndpoint) && IsMissing(configuration[$"{FederatedConnection}:TenantId"]))
        {
            missing.Add($"{FederatedConnection}:TenantId (or AuthorityEndpoint)");
        }
        else if (!string.IsNullOrWhiteSpace(authorityEndpoint) && IsMissing(authorityEndpoint))
        {
            // An AuthorityEndpoint left as "<tenant-id>" wins over a valid TenantId and makes every token request fail.
            missing.Add($"{FederatedConnection}:AuthorityEndpoint (placeholder; remove it or set the real authority)");
        }

        // With no mapping every person resolves to "unknown member", which looks like a bug in the bot, not in setup.
        if (!configuration.GetSection("Bot:Members:ByObjectId").GetChildren().Any(c => !IsMissing(c.Value)))
        {
            missing.Add("Bot:Members:ByObjectId (at least one entry; infra/scripts/Set-MemberMapping.ps1)");
        }

        // A mistyped route would silently run a different set of branches, so it is reported like any missing setting.
        if (SharePointRouteSetting.TryRead(configuration) is null)
        {
            missing.Add(SharePointRouteSetting.InvalidLabel);
        }

        // Same for the route A relevance cut and summary switch: a typo must not quietly disable the cut or the summary.
        if (!RetrievalSettingsReader.TryReadMinRelevance(configuration, out _))
        {
            missing.Add(RetrievalSettingsReader.InvalidMinRelevanceLabel);
        }

        if (!RetrievalSettingsReader.TryReadSummarise(configuration, out var summarise))
        {
            missing.Add(RetrievalSettingsReader.InvalidSummariseLabel);
        }
        else if (summarise && SharePointRouteSetting.TryRead(configuration) is SharePointRoute.Retrieval or SharePointRoute.Both
            && IsMissing(configuration["Bot:Foundry:AnswerAgent:AgentName"]))
        {
            // The agent name has a code default, so this only trips when it was set to blank/placeholder on purpose.
            missing.Add("Bot:Foundry:AnswerAgent:AgentName");
        }

        return missing;
    }

    /// <summary>Template values look like <c>&lt;tenant-id&gt;</c>; anything still wrapped in angle brackets was never filled in.</summary>
    private static bool IsMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) || (value.Contains('<') && value.Contains('>'));
}
