using System.ClientModel.Primitives;

namespace Compass.Agents.Prompt.SharePointKnowledge.Foundry;

/// <summary>
/// Points a Responses call at a hosted agent's own endpoint. The SDK's <c>ProjectResponsesClient</c> posts to the
/// project-level <c>/openai/v1/responses</c> route, which is correct for prompt agents but which Foundry rejects for
/// hosted agents with <c>400 "Hosted agents can only be called through the agent endpoint"</c>. A hosted agent is
/// reached at <c>/agents/{name}/endpoint/protocols/openai/responses</c>. The body (including <c>agent_reference</c>) is
/// accepted unchanged there, so only the URI is rewritten.
/// </summary>
internal sealed class HostedAgentEndpointPolicy(string agentName) : PipelinePolicy
{
    private const string ProjectRoute = "/openai/v1/responses";
    private const string ApiVersion = "v1";

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Rewrite(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Rewrite(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void Rewrite(PipelineMessage message)
    {
        var uri = message.Request.Uri
            ?? throw new InvalidOperationException("The request has no URI to route to the hosted agent endpoint.");

        var path = uri.AbsolutePath;
        if (!path.EndsWith(ProjectRoute, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Only a create-response call can be routed to a hosted agent endpoint; got {path}.");
        }

        var projectPath = path[..^ProjectRoute.Length];
        message.Request.Uri = new UriBuilder(uri)
        {
            Path = $"{projectPath}/agents/{Uri.EscapeDataString(agentName)}/endpoint/protocols/openai/responses",
            Query = $"api-version={ApiVersion}",
        }.Uri;
    }
}
