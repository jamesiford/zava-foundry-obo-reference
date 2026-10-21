namespace Compass.Agents.Contracts.Exceptions;

/// <summary>
/// An agent was called without the downstream token it needs. Carries the header <em>name</em> only — never a
/// token value — so the message is always safe to log and to return to the caller.
/// </summary>
public sealed class MissingDownstreamTokenException(string headerName)
    : Exception($"Required downstream token header '{headerName}' is missing; refusing to call the downstream service.")
{
    public string HeaderName { get; } = headerName;
}
