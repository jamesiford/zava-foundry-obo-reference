using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Exceptions;

namespace Compass.Bot.Host.Turns;

/// <summary>
/// Classifies a failed turn once, so Teams and the web front door agree on which failures mean "sign in again".
/// </summary>
public static class BriefingFailure
{
    /// <summary>
    /// True when the person (or the token they brought) is the problem rather than a service; the fix is to sign in again.
    /// </summary>
    public static bool IsAuthentication(Exception ex) =>
        ex is PersonResolutionException
            or TokenAcquisitionException
            or MissingDownstreamTokenException
            or DownstreamAuthenticationException
            or ClaimsChallengeException;
}
