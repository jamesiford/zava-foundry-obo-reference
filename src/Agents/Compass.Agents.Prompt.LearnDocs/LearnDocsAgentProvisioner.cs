using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.LearnDocs;

/// <summary>
/// Publishes the definition to the Foundry project as a new agent version. Versions are immutable, so re-running
/// simply adds one. Creating agents is an administrative act, so it runs as the operator's identity.
/// </summary>
public sealed class LearnDocsAgentProvisioner(AIProjectClient project, IOptions<LearnDocsAgentOptions> options)
{
    private readonly LearnDocsAgentOptions _options = options.Value;

    public async Task<ProjectsAgentVersion> PublishAsync(CancellationToken cancellationToken)
    {
        var creation = new ProjectsAgentVersionCreationOptions(LearnDocsAgentDefinition.Build(_options))
        {
            Description = "Prompt agent with the Microsoft Learn remote MCP server as its only tool.",
        };

        var result = await project.AgentAdministrationClient.CreateAgentVersionAsync(_options.AgentName, creation, cancellationToken: cancellationToken);
        return result.Value;
    }
}
