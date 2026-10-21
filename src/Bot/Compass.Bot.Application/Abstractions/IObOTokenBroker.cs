using Compass.Bot.Domain;

namespace Compass.Bot.Application.Abstractions;

/// <summary>
/// Exchanges the person's token for one scoped to a downstream API (OAuth on-behalf-of). Defined here and implemented
/// in Infrastructure so the orchestration can be tested without Entra (dev spec §5, dependency rule).
/// </summary>
public interface IObOTokenBroker
{
    /// <exception cref="Exceptions.TokenAcquisitionException">Entra refused or needs interaction (e.g. a claims challenge).</exception>
    Task<AccessToken> AcquireAsync(AccessToken userAssertion, DownstreamAudience audience, CancellationToken cancellationToken);
}
