using Compass.Bot.Domain;

namespace Compass.Bot.Application.Abstractions;

/// <summary>Which hosted agent a call targets.</summary>
public enum HostedAgentName
{
    SharePointRetrieval,
    ZavaApi,

    /// <summary>The Foundry prompt agent backed by the Foundry IQ SharePoint knowledge base; called with the person's Search token.</summary>
    SharePointKnowledge,
}

/// <summary>
/// Everything one hosted-agent call needs. <paramref name="ForwardedTokens"/> is keyed by the <c>x-client-*</c> header
/// that must carry each token, so the gateway forwards them verbatim and never has to know which token is which.
/// </summary>
public sealed record HostedAgentCall(
    HostedAgentName Agent,
    PersonContext Person,
    SessionId Session,
    IReadOnlyDictionary<string, AccessToken> ForwardedTokens);

/// <summary>
/// Calls a hosted agent in Foundry. The only abstraction that may assert a person's identity to Foundry
/// (contract rule 3).
/// </summary>
public interface IHostedAgentGateway
{
    /// <exception cref="Compass.Agents.Contracts.Exceptions.MissingDownstreamTokenException">The agent reported a missing token.</exception>
    /// <exception cref="Compass.Agents.Contracts.Exceptions.DownstreamAuthenticationException">The agent's downstream rejected the token.</exception>
    /// <exception cref="Compass.Agents.Contracts.Exceptions.ClaimsChallengeException">The downstream demanded step-up authentication.</exception>
    Task<TResponse> InvokeAsync<TRequest, TResponse>(HostedAgentCall call, TRequest request, CancellationToken cancellationToken);
}
