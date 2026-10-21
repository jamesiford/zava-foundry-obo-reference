using Compass.Bot.Domain;

namespace Compass.Bot.Application.Exceptions;

/// <summary>
/// The on-behalf-of exchange failed. Carries a claims challenge when Entra wants step-up so the host can re-prompt;
/// never carries a token.
/// </summary>
public sealed class TokenAcquisitionException(DownstreamAudience audience, string message, string? claims = null, Exception? inner = null)
    : Exception(message, inner)
{
    public DownstreamAudience Audience { get; } = audience;

    public string? Claims { get; } = claims;
}
