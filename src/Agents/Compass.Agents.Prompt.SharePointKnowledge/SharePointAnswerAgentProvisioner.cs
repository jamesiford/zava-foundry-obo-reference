using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Publishes the answer agent definition to the Foundry project as a new agent version (<c>--publish-answer-agent</c>).
/// Like the knowledge agent's provisioner it needs the operator's identity, not the bot's, and re-running only adds a version.
/// </summary>
public sealed class SharePointAnswerAgentProvisioner(AIProjectClient project, IOptions<AnswerAgentOptions> options)
{
    private readonly AnswerAgentOptions _options = options.Value;

    public async Task<ProjectsAgentVersion> PublishAsync(CancellationToken cancellationToken)
    {
        var creation = new ProjectsAgentVersionCreationOptions(SharePointAnswerAgentDefinition.Build(_options))
        {
            Description = "Writes a short, cited answer from extracts the bot fetched as the signed-in person. No tools.",
        };

        var result = await project.AgentAdministrationClient.CreateAgentVersionAsync(_options.AgentName, creation, cancellationToken: cancellationToken);
        return result.Value;
    }
}
