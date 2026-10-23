using System.ClientModel;
using Azure.AI.Extensions.OpenAI;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Sends one prompt to the SharePoint answer agent and returns its text. A new, unstored, single-turn call every time: no
/// stored conversation, no <c>previous_response_id</c>, no per-person header, no structured input, and no cache, so one
/// person's extracts cannot reach another person's call.
/// </summary>
public sealed class SharePointAnswerAgent(IFoundryResponsesClientFactory clients, IOptions<AnswerAgentOptions> options) : IAnswerAgent
{
    private readonly AnswerAgentOptions _options = options.Value;

    /// <exception cref="AnswerAgentException">Foundry rejected the call or the agent failed. The message holds a status and code only.</exception>
    public async Task<string> AnswerAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);

        var client = clients.Create(new AgentReference(_options.AgentName, _options.AgentVersion));

        var create = new CreateResponseOptions { StoredOutputEnabled = false };
        create.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

        ResponseResult response;
        try
        {
            response = (await client.CreateResponseAsync(create, cancellationToken)).Value;
        }
        catch (ClientResultException ex)
        {
            // ex.Message can echo the request; surface only the status.
            throw new AnswerAgentException(ex.Status, code: null);
        }

        if (response.Status == ResponseStatus.Failed || response.Error is not null)
        {
            throw new AnswerAgentException(statusCode: 502, response.Error?.Code.ToString());
        }

        return response.GetOutputText();
    }
}
