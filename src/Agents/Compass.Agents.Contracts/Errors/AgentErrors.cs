using Compass.Agents.Contracts.Exceptions;

namespace Compass.Agents.Contracts.Errors;

/// <summary>The JSON body a hosted agent returns on failure. Never contains a token — only a code, a safe message and, for step-up, the claims challenge.</summary>
public sealed record AgentError(string Code, string Message, string? Claims = null);

/// <summary>Stable error codes shared by the hosted agents (producer) and the bot's adapters (consumer).</summary>
public static class AgentErrorCodes
{
    public const string MissingToken = "missing_token";

    public const string TokenRejected = "token_rejected";

    public const string ClaimsChallenge = "claims_challenge";
}

/// <summary>
/// Translates between agent exceptions and the HTTP error contract in both directions, so the hosts that emit errors
/// and the bot that reads them can't drift apart (dev spec §6, DRY). Every failure stays typed end to end: a missing,
/// rejected or challenged token must reach the bot as such rather than being flattened into a generic 500.
/// </summary>
public static class AgentErrors
{
    /// <summary>Maps an agent exception to an HTTP status and body, or <c>null</c> for failures that aren't auth-related.</summary>
    public static (int StatusCode, AgentError Error)? FromException(Exception exception) => exception switch
    {
        MissingDownstreamTokenException m => (401, new AgentError(AgentErrorCodes.MissingToken, m.Message)),
        ClaimsChallengeException c => (401, new AgentError(AgentErrorCodes.ClaimsChallenge, c.Message, c.Claims)),
        DownstreamAuthenticationException d => (401, new AgentError(AgentErrorCodes.TokenRejected, d.Message)),
        _ => null,
    };

    /// <summary>
    /// Folds the claims challenge into the message so it survives the Foundry hosting adapter, which keeps only
    /// <c>error.code</c> and <c>error.message</c> on a failed response and drops <c>param</c>. Reversed by <see cref="Unpack"/>.
    /// </summary>
    public static string PackMessage(AgentError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return string.IsNullOrEmpty(error.Claims) ? error.Message : $"{error.Message}{ClaimsMarker}{error.Claims}]";
    }

    /// <summary>Rebuilds an <see cref="AgentError"/> from the <c>code</c> and <c>message</c> of a failed response.</summary>
    public static AgentError Unpack(string code, string? message)
    {
        message ??= string.Empty;
        var at = message.LastIndexOf(ClaimsMarker, StringComparison.Ordinal);
        if (at < 0 || !message.EndsWith(']'))
        {
            return new AgentError(code, message);
        }

        var claims = message[(at + ClaimsMarker.Length)..^1];
        return new AgentError(code, message[..at], claims.Length == 0 ? null : claims);
    }

    private const string ClaimsMarker = " [claims:";

    /// <summary>
    /// Rebuilds the typed exception from a received error body, or <c>null</c> when the code is unknown.
    /// </summary>
    /// <param name="downstream">Friendly name of the agent/service that reported the error.</param>
    /// <param name="headerName">The header the agent says it was missing, used only for the missing-token case.</param>
    public static Exception? ToException(string downstream, string headerName, int statusCode, AgentError error) => error.Code switch
    {
        AgentErrorCodes.MissingToken => new MissingDownstreamTokenException(headerName),
        AgentErrorCodes.ClaimsChallenge when !string.IsNullOrEmpty(error.Claims) => new ClaimsChallengeException(downstream, error.Claims),
        AgentErrorCodes.TokenRejected => new DownstreamAuthenticationException(downstream, statusCode),
        _ => null,
    };
}
