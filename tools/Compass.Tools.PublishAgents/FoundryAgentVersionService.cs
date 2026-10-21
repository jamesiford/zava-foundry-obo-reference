using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.Projects.Agents;
using Azure.Core;

namespace Compass.Tools.PublishAgents;

/// <summary>
/// The Foundry implementation. Authenticates with whatever credential it is given (Azure CLI / azd login locally), so the
/// tool never holds a secret of its own.
/// </summary>
public sealed class FoundryAgentVersionService(Uri projectEndpoint, TokenCredential credential) : IAgentVersionService
{
    private readonly AgentAdministrationClient _client = new(projectEndpoint, credential);

    public async Task<AgentVersionState> CreateFromFolderAsync(HostedAgentSpec spec, string folder, CancellationToken cancellationToken)
    {
        var version = await _client.CreateAgentVersionFromCodeAsync(
            spec.Name, folder, HostedAgentDefinitionBuilder.BuildMetadata(spec), cancellationToken);
        return ToState(version);
    }

    public async Task<AgentVersionState> GetAsync(string name, string version, CancellationToken cancellationToken)
    {
        var response = await _client.GetAgentVersionAsync(name, version, cancellationToken);
        return ToState(response.Value);
    }

    private static AgentVersionState ToState(ProjectsAgentVersion version) =>
        new(version.Name, version.Version, version.Status?.ToString().ToLowerInvariant() ?? "unknown", ExtractError(version));

    /// <summary>
    /// The prerelease model has no typed error, but the service returns an <c>error</c> object on a failed version. It is kept
    /// in the model's additional properties, so read it from the wire form rather than lose the only diagnostic we get.
    /// </summary>
    private static string? ExtractError(ProjectsAgentVersion version)
    {
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(version, ModelReaderWriterOptions.Json));
        return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null
            ? error.ToString()
            : null;
    }
}
