using Azure.AI.Extensions.OpenAI;

namespace Compass.Agents.Prompt.SharePointKnowledge.Foundry;

/// <summary>
/// Creates a Responses client bound to one agent in the Foundry project. A new client per call is deliberate: any
/// per-call headers live on that client's pipeline and die with it, so there is no shared mutable state to leak
/// identity across people.
/// </summary>
public interface IFoundryResponsesClientFactory
{
    ProjectResponsesClient Create(AgentReference agent, IReadOnlyDictionary<string, string>? requestHeaders = null);

    /// <summary>
    /// Like <see cref="Create"/> but routed to a hosted agent's own endpoint, the only route Foundry accepts for a
    /// hosted (container) agent.
    /// </summary>
    ProjectResponsesClient CreateForHostedAgent(AgentReference agent, IReadOnlyDictionary<string, string>? requestHeaders = null);
}
