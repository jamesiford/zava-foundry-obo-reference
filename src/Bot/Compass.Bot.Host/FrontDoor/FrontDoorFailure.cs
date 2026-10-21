using System.Text;
using System.Text.RegularExpressions;
using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Host.Turns;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>How a failed briefing is reported to the browser: an HTTP status before the stream starts, a typed event after.</summary>
/// <param name="Status">HTTP status used when no event has been written yet.</param>
/// <param name="Code">Stable, machine-readable reason; the page maps it to wording. Never carries exception text.</param>
/// <param name="Claims">For a step-up demand only: the base64 claims request the page hands back to Entra.</param>
public sealed partial record FrontDoorFailure(int Status, string Code, string? Claims = null)
{
    public const string SignInRequired = "sign_in_required";
    public const string ClaimsChallenge = "claims_challenge";
    public const string MemberNotLinked = "member_not_linked";
    public const string ServiceUnavailable = "service_unavailable";
    public const string Timeout = "timeout";
    public const string InvalidRequest = "invalid_request";
    public const string InternalError = "internal_error";

    /// <summary>The <c>WWW-Authenticate</c> value for a 401, or <see langword="null"/> for other statuses.</summary>
    public string? Challenge => Status != StatusCodes.Status401Unauthorized
        ? null
        : Code == ClaimsChallenge && Claims is not null
            ? $"Bearer error=\"insufficient_claims\", claims=\"{Claims}\""
            : "Bearer error=\"invalid_token\"";

    public static FrontDoorFailure From(Exception exception, bool timedOut = false)
    {
        if (timedOut)
        {
            return new FrontDoorFailure(StatusCodes.Status504GatewayTimeout, Timeout);
        }

        return exception switch
        {
            ClaimsChallengeException challenge => new FrontDoorFailure(StatusCodes.Status401Unauthorized, ClaimsChallenge, SafeClaims(challenge.Claims)),
            TokenAcquisitionException { Claims: { Length: > 0 } claims } => new FrontDoorFailure(StatusCodes.Status401Unauthorized, ClaimsChallenge, SafeClaims(ToWireClaims(claims))),
            _ when BriefingFailure.IsAuthentication(exception) => new FrontDoorFailure(StatusCodes.Status401Unauthorized, SignInRequired),
            MemberNotLinkedException => new FrontDoorFailure(StatusCodes.Status403Forbidden, MemberNotLinked),
            HostedAgentException => new FrontDoorFailure(StatusCodes.Status502BadGateway, ServiceUnavailable),
            _ => new FrontDoorFailure(StatusCodes.Status500InternalServerError, InternalError),
        };
    }

    // The value is echoed into a response header, so anything that is not plain base64/base64url is dropped, not escaped.
    private static string? SafeClaims(string claims) => Base64Shape().IsMatch(claims) ? claims : null;

    // MSAL reports a step-up demand as decoded JSON; the wire (and WWW-Authenticate) carries it base64-encoded.
    private static string ToWireClaims(string claims) =>
        claims.TrimStart().StartsWith('{') ? Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)) : claims;

    [GeneratedRegex("^[A-Za-z0-9+/_=-]{1,4096}$")]
    private static partial Regex Base64Shape();
}
