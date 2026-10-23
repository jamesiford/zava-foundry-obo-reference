using Azure.Core;
using Azure.Identity;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Infrastructure.Foundry;
using Compass.Bot.Infrastructure.Identity;
using Compass.Bot.Infrastructure.Members;
using Compass.Bot.Infrastructure.Obo;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Bot.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Wires the MSAL OBO broker, the Foundry gateway, Teams SSO validation and the member directory.</summary>
    public static IServiceCollection AddCompassBotInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OnBehalfOfOptions>()
            .Bind(configuration.GetSection(OnBehalfOfOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId) && !string.IsNullOrWhiteSpace(o.ClientId), "Bot:Obo TenantId and ClientId are required.")
            .ValidateOnStart();
        services.AddOptions<FoundryOptions>().Bind(configuration.GetSection(FoundryOptions.SectionName));
        services.AddOptions<FoundryProjectOptions>().Bind(configuration.GetSection(FoundryProjectOptions.SectionName));
        services.AddOptions<KnowledgeAgentOptions>().Bind(configuration.GetSection(KnowledgeAgentOptions.SectionName));
        services.AddOptions<AnswerAgentOptions>().Bind(configuration.GetSection(AnswerAgentOptions.SectionName));
        services.AddOptions<MemberDirectoryOptions>().Bind(configuration.GetSection(MemberDirectoryOptions.SectionName));

        // The Teams SSO token is validated with the same audience/issuer validator the MCP server uses; only the
        // accepted audience (the bot's own app ID URI) differs.
        services.AddJwtAccessTokenValidation(o => configuration.GetSection("Bot:Auth").Bind(o));

        services.TryAddSingleton<IClientAssertionProvider, ManagedIdentityClientAssertionProvider>();
        services.TryAddSingleton<IOnBehalfOfClient, MsalOnBehalfOfClient>();
        services.TryAddSingleton<IObOTokenBroker, OboTokenBroker>();
        services.TryAddSingleton<IPersonContextResolver, TeamsSsoPersonContextResolver>();
        services.TryAddSingleton<IMemberNumberResolver, ConfiguredMemberNumberResolver>();
        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        services.AddSharePointKnowledgeAgent();
        services.AddSingleton<IAnswerFromExtracts, FoundryExtractAnswerer>();
        services.AddSingleton<IHostedAgentGateway, FoundryHostedAgentGateway>();

        return services;
    }
}
