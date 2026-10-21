using System.Text.RegularExpressions;

namespace Compass.Shared.Observability.Redaction;

/// <summary>
/// Scrubs JWT-shaped strings from text before it reaches a log, trace or exception sink (dev spec §9, §13).
/// </summary>
/// <remarks>
/// Defence in depth: the primary control is never putting a token in a message at all (enforced by tests). This is
/// the backstop for the day a library, a debugger-style <c>ToString()</c> or a copy-paste logs one anyway. It matches
/// the compact JWS shape (three base64url segments, header starting <c>eyJ</c>) rather than any long string, so
/// ordinary identifiers and URLs are left alone.
/// </remarks>
public static partial class JwtRedactor
{
    public const string Replacement = "[REDACTED-TOKEN]";

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex JwtShape();

    /// <summary>True when <paramref name="text"/> contains something shaped like a JWT.</summary>
    public static bool ContainsToken(string? text) => !string.IsNullOrEmpty(text) && JwtShape().IsMatch(text);

    /// <summary>Returns <paramref name="text"/> with every JWT-shaped substring replaced.</summary>
    public static string? Redact(string? text) =>
        string.IsNullOrEmpty(text) ? text : JwtShape().Replace(text, Replacement);
}
