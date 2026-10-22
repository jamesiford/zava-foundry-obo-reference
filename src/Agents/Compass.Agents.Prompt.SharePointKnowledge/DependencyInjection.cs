using Azure.AI.Projects;
using Azure.Core;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.SharePointKnowledge;

public static class DependencyInjection
{
    /// <summary>Registers the knowledge agent, its Foundry client factory and the provisioner. Expects a <see cref="TokenCredential"/> and bound options.</summary>
    public static IServiceCollection AddSharePointKnowledgeAgent(this IServiceCollection services)
    {
        services.TryAddSingleton<IFoundryResponsesClientFactory, FoundryResponsesClientFactory>();
        services.TryAddSingleton<IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>, SharePointKnowledgeAgent>();
        services.TryAddSingleton(sp =>
        {
            var endpoint = sp.GetRequiredService<IOptions<FoundryProjectOptions>>().Value.ProjectEndpoint
                ?? throw new InvalidOperationException($"{FoundryProjectOptions.SectionName}:ProjectEndpoint is not configured.");
            return new AIProjectClient(endpoint, sp.GetRequiredService<TokenCredential>());
        });
        services.TryAddSingleton<SharePointKnowledgeAgentProvisioner>();
        services.TryAddSingleton<IAnswerAgent, SharePointAnswerAgent>();
        services.TryAddSingleton<SharePointAnswerAgentProvisioner>();
        return services;
    }
}
