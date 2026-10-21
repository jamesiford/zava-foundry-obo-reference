using Compass.Shared.Observability.Redaction;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// Scans captured text for two kinds of leak: anything shaped like a JWT, and the exact value of every token the test
/// run minted. The shape check catches tokens the test didn't know about; the exact check catches a token that has
/// been mangled into a different shape but is still recoverable.
/// </summary>
internal static class LeakDetector
{
    public static IReadOnlyList<string> Find(IEnumerable<string> captured, IEnumerable<string> knownTokens)
    {
        var tokens = knownTokens.ToArray();
        return captured
            .Where(line => JwtRedactor.ContainsToken(line) || tokens.Any(t => line.Contains(t, StringComparison.Ordinal)))
            .ToArray();
    }
}
