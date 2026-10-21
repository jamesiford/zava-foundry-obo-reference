using System.ClientModel;
using Azure.AI.Extensions.OpenAI;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Responses;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Compass.Bot.Infrastructure.Foundry;

/// <summary>
/// Calls a hosted agent as the bot (hop 3) through the Foundry SDK v2 Responses client, routed to the agent's own
/// endpoint (Foundry rejects hosted agents on the project-level responses route). This is the only class in the
/// solution that sets <c>x-ms-user-identity</c>: Foundry honours that header only from a caller holding the
/// impersonation role, so keeping it in one place is what makes "only the bot may assert a person" reviewable
/// (contract rule 3).
/// </summary>
/// <remarks>
/// The SDK client authenticates with the bot's own token (<c>Authorization</c>); the person is asserted in
/// <c>x-ms-user-identity</c> and each downstream token rides in its own <c>x-client-*</c> header with no <c>Bearer</c>
/// prefix. A client is created per call, so those headers live on one pipeline and cannot reach another person's call.
/// </remarks>
public sealed class FoundryHostedAgentGateway(
    IFoundryResponsesClientFactory clients,
    IOptions<FoundryOptions> options,
    IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult> knowledgeAgent) : IHostedAgentGateway
{
    private readonly FoundryOptions _options = options.Value;

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(HostedAgentCall call, TRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (call.Agent == HostedAgentName.SharePointKnowledge)
        {
            return await InvokeKnowledgeAgentAsync<TRequest, TResponse>(call, request, cancellationToken);
        }

        var reference = call.Agent switch
        {
            HostedAgentName.SharePointRetrieval => _options.SharePointRetrievalAgent,
            HostedAgentName.ZavaApi => _options.ZavaApiAgent,
            _ => null,
        };
        if (reference is null || string.IsNullOrWhiteSpace(reference.Name))
        {
            throw new InvalidOperationException($"No Foundry agent is configured for hosted agent {call.Agent}.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompassHeaders.UserIdentity] = call.Person.ObjectId,
            [CompassHeaders.SessionId] = call.Session.Value,
        };
        foreach (var (header, token) in call.ForwardedTokens)
        {
            headers[header] = token.Reveal();
        }

        var client = clients.CreateForHostedAgent(new AgentReference(reference.Name, string.IsNullOrWhiteSpace(reference.Version) ? null : reference.Version), headers);

        var create = new CreateResponseOptions { StoredOutputEnabled = false };
        create.InputItems.Add(ResponseItem.CreateUserMessageItem(ResponsesWire.ToText(request)));

        ResponseResult response;
        try
        {
            response = (await client.CreateResponseAsync(create, cancellationToken)).Value;
        }
        catch (ClientResultException ex)
        {
            throw MapFailure(call, ex);
        }

        if (response.Status == ResponseStatus.Failed || response.Error is not null)
        {
            throw MapFailedResponse(call, response);
        }

        return ResponsesWire.FromText<TResponse>(response.GetOutputText())
            ?? throw new HostedAgentException(call.Agent, 502);
    }

    /// <summary>
    /// The knowledge agent is a Foundry <em>prompt</em> agent, not a hosted container: there is no per-agent endpoint and
    /// no <c>x-ms-user-identity</c> to assert. The person's identity reaches the knowledge base solely as their Search
    /// token (a structured input), so the call is delegated to the agent class with the forwarded token unchanged.
    /// </summary>
    private async Task<TResponse> InvokeKnowledgeAgentAsync<TRequest, TResponse>(HostedAgentCall call, TRequest request, CancellationToken cancellationToken)
    {
        if (request is not KnowledgeQueryRequest knowledgeRequest)
        {
            throw new ArgumentException($"{call.Agent} accepts only {nameof(KnowledgeQueryRequest)}.", nameof(request));
        }

        var tokens = call.ForwardedTokens.ToDictionary(t => t.Key, t => t.Value.Reveal(), StringComparer.OrdinalIgnoreCase);
        var context = new AgentCallContext(call.Person.ObjectId, tokens);

        try
        {
            var result = await knowledgeAgent.HandleAsync(knowledgeRequest, context, cancellationToken);
            return (TResponse)(object)result;
        }
        catch (KnowledgeAgentException ex)
        {
            throw new HostedAgentException(call.Agent, ex.StatusCode);
        }
    }

    // The hosting adapter reports an agent's typed failure as HTTP 200 with status "failed" and only error.code/message,
    // so that — not an HTTP 401 — is where a missing, rejected or challenged token arrives. Anything else stays a 502.
    private static Exception MapFailedResponse(HostedAgentCall call, ResponseResult response)
    {
        var code = response.Error?.Code.ToString();
        if (string.IsNullOrEmpty(code))
        {
            return new HostedAgentException(call.Agent, 502);
        }

        var error = AgentErrors.Unpack(code, response.Error!.Message);
        var header = call.ForwardedTokens.Keys.FirstOrDefault() ?? "unknown";
        return AgentErrors.ToException(call.Agent.ToString(), header, 401, error)
            ?? new HostedAgentException(call.Agent, 502);
    }

    // ex.Message can echo the request, so only the status and the agent's own typed error body are used.
    private static Exception MapFailure(HostedAgentCall call, ClientResultException ex)
    {
        if (ex.Status != 401)
        {
            return new HostedAgentException(call.Agent, ex.Status);
        }

        // A non-JSON 401 (for example from a gateway in front of the agent) is still an auth failure.
        var error = ResponsesWire.ReadError(ex.GetRawResponse()?.Content?.ToString());

        var header = call.ForwardedTokens.Keys.FirstOrDefault() ?? "unknown";
        var name = call.Agent.ToString();
        return (error is null ? null : AgentErrors.ToException(name, header, ex.Status, error))
            ?? new DownstreamAuthenticationException(name, ex.Status);
    }
}
