using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Publishes the knowledge agent definition to the Foundry project as a new agent version. Idempotent by design:
/// versions are immutable, so re-running simply adds a version and the bot (invoking the latest) picks it up.
/// </summary>
/// <remarks>
/// Run once per environment after the knowledge base, project connection and RBAC exist (see infra/README.md). It
/// needs the operator's or pipeline's identity — not the bot's — because creating agents is an administrative act.
/// </remarks>
public sealed class SharePointKnowledgeAgentProvisioner(AIProjectClient project, IOptions<KnowledgeAgentOptions> options)
{
    private readonly KnowledgeAgentOptions _options = options.Value;

    public async Task<ProjectsAgentVersion> PublishAsync(CancellationToken cancellationToken)
    {
        var definition = SharePointKnowledgeAgentDefinition.Build(_options);
        var creation = new ProjectsAgentVersionCreationOptions(definition)
        {
            Description = "Answers questions from the Foundry IQ SharePoint knowledge base as the signed-in person.",
        };

        var result = await project.AgentAdministrationClient.CreateAgentVersionAsync(_options.AgentName, creation, cancellationToken: cancellationToken);
        return result.Value;
    }
}
