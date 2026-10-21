using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Compass.Agents.Prompt.LearnDocs;

/// <summary>Asks the published agent one question. <c>StoredOutputEnabled = false</c> keeps the demo from storing responses.</summary>
public sealed class LearnDocsAgentClient(Uri projectEndpoint, TokenCredential credential, IOptions<LearnDocsAgentOptions> options)
{
    private readonly LearnDocsAgentOptions _options = options.Value;

    /// <param name="question">What to ask.</param>
    /// <param name="version">Agent version to invoke; <c>null</c> invokes the latest.</param>
    public async Task<string> AskAsync(string question, string? version = null, CancellationToken cancellationToken = default)
    {
        var request = new CreateResponseOptions { StoredOutputEnabled = false };
        request.InputItems.Add(ResponseItem.CreateUserMessageItem(question));

        var responses = new ProjectResponsesClient(projectEndpoint, credential, new AgentReference(_options.AgentName, version));
        var result = (await responses.CreateResponseAsync(request, cancellationToken)).Value;
        return result.GetOutputText();
    }
}
