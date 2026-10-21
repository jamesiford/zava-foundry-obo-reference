namespace Compass.Agents.Contracts.Exceptions;

/// <summary>
/// A downstream service rejected the person's token as expired or invalid. Typed so the bot can run a fresh OBO
/// exchange (or ask the person to sign in again) instead of retrying the same token or falling back to app-only
/// credentials (contract rule 5).
/// </summary>
public sealed class DownstreamAuthenticationException(string downstream, int statusCode)
    : Exception($"{downstream} rejected the person's token (HTTP {statusCode}).")
{
    public string Downstream { get; } = downstream;

    public int StatusCode { get; } = statusCode;
}
