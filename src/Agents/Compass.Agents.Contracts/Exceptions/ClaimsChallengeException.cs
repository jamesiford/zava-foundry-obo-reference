namespace Compass.Agents.Contracts.Exceptions;

/// <summary>
/// A downstream service demanded step-up authentication (a Conditional Access claims challenge). It must travel all
/// the way back to the bot so the person is asked to re-authenticate in Teams; swallowing it would show the person
/// an empty or generic failure and leave them unable to fix it.
/// </summary>
/// <remarks>The challenge string is a base64 claims request, not a credential, so it is safe to carry and log.</remarks>
public sealed class ClaimsChallengeException(string downstream, string claims)
    : Exception($"{downstream} requires additional authentication (claims challenge).")
{
    public string Downstream { get; } = downstream;

    public string Claims { get; } = claims;
}
