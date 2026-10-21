using Compass.Agents.Contracts.Exceptions;

namespace Compass.Agents.Contracts.Context;

/// <summary>Shared token access for agents so the fail-closed check is written once (dev spec §6, DRY).</summary>
public static class AgentCallContextExtensions
{
    /// <summary>
    /// Returns the token forwarded under <paramref name="headerName"/>, or throws. Fails closed — see Compass
    /// identity contract rule 5: an agent without the person's token must stop, never fall back to an app-only
    /// identity that could see more than the person is allowed to.
    /// </summary>
    /// <exception cref="MissingDownstreamTokenException">The header is absent or blank.</exception>
    public static string RequireToken(this AgentCallContext context, string headerName)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.ForwardedTokens.TryGetValue(headerName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            throw new MissingDownstreamTokenException(headerName);
        }

        return token;
    }
}
