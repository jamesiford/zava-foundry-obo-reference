using System.Text.RegularExpressions;

namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Reads the Conditional Access claims challenge from a <c>WWW-Authenticate</c> header (the CAE / step-up pattern:
/// <c>Bearer error="insufficient_claims", claims="eyJ…"</c>). Shared so every adapter that calls a downstream API
/// recognises a challenge the same way.
/// </summary>
public static partial class ClaimsChallengeParser
{
    [GeneratedRegex("claims=\"(?<claims>[^\"]+)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ClaimsParameter();

    /// <summary>True, with the challenge's claims value, when any header value carries one.</summary>
    public static bool TryGetClaims(IEnumerable<string> wwwAuthenticateValues, out string claims)
    {
        foreach (var value in wwwAuthenticateValues)
        {
            var match = ClaimsParameter().Match(value);
            if (match.Success)
            {
                claims = match.Groups["claims"].Value;
                return true;
            }
        }

        claims = string.Empty;
        return false;
    }
}
