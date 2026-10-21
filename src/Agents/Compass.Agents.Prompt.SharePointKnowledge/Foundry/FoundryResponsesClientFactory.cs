using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Compass.Agents.Prompt.SharePointKnowledge.Foundry;

/// <summary>
/// The Foundry SDK v2 <see cref="ProjectResponsesClient"/>, authenticated as the calling workload (the bot's managed
/// identity), never as the person. The person's token travels only as a structured input or a header, never as the
/// client credential.
/// </summary>
/// <param name="transport">Test seam: lets a test capture the real outgoing request. <c>null</c> uses the SDK default.</param>
public sealed class FoundryResponsesClientFactory(
    IOptions<FoundryProjectOptions> options,
    TokenCredential credential,
    PipelineTransport? transport = null) : IFoundryResponsesClientFactory
{
    public ProjectResponsesClient Create(AgentReference agent, IReadOnlyDictionary<string, string>? requestHeaders = null) =>
        Build(agent, requestHeaders, hosted: false);

    public ProjectResponsesClient CreateForHostedAgent(AgentReference agent, IReadOnlyDictionary<string, string>? requestHeaders = null) =>
        Build(agent, requestHeaders, hosted: true);

    private ProjectResponsesClient Build(AgentReference agent, IReadOnlyDictionary<string, string>? requestHeaders, bool hosted)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var endpoint = options.Value.ProjectEndpoint
            ?? throw new InvalidOperationException($"{FoundryProjectOptions.SectionName}:ProjectEndpoint is not configured.");

        var clientOptions = new ProjectResponsesClientOptions();
        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

        if (hosted)
        {
            clientOptions.AddPolicy(new HostedAgentEndpointPolicy(agent.Name), PipelinePosition.PerCall);
        }

        if (requestHeaders is { Count: > 0 })
        {
            clientOptions.AddPolicy(new StaticHeadersPolicy(requestHeaders), PipelinePosition.PerCall);
        }

        return new ProjectResponsesClient(endpoint, credential, agent, null, clientOptions);
    }
}
