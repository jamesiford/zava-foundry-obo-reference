using Compass.Agents.Contracts.Context;

namespace Compass.Agents.Contracts;

/// <summary>
/// The single shape every hosted agent implements, so the bot and the container hosts can treat agents uniformly
/// and a new agent is a new class rather than a new branch (dev spec §6, Open/Closed).
/// </summary>
/// <remarks>
/// Liskov contract for every implementation: fail closed when the token it needs is missing, never convert a
/// downstream failure into a "success" result, and never log a raw token.
/// </remarks>
public interface IHostedAgent<in TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, AgentCallContext context, CancellationToken ct);
}
