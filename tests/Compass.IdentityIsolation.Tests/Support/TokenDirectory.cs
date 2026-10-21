using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Compass.Agents.Contracts.Exceptions;
using Compass.Shared.Identity.Tokens;

namespace Compass.IdentityIsolation.Tests.Support;

internal enum TokenState
{
    Valid,
    Expired,
    ClaimsChallenge,

    /// <summary>A token whose issuer is not the configured tenant; only the Teams/web validator looks at it.</summary>
    WrongIssuer,
}

internal sealed record TokenGrant(TestPerson Person, string Audience, TokenState State);

/// <summary>
/// Stands in for Entra: every token the fake broker issues is registered here against the person and audience it was
/// minted for. The fake downstream services look tokens up here, so "who is this token for" is answered the way a real
/// resource server answers it — from the token, never from anything else on the request.
/// </summary>
internal sealed class TokenDirectory
{
    public const string SsoAudience = "sso";

    public const string ClaimsChallenge = "eyJhY2Nlc3NfdG9rZW4iOnsibmJmIjp7ImVzc2VudGlhbCI6dHJ1ZX19fQ==";

    private readonly ConcurrentDictionary<string, TokenGrant> _grants = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TokenState>> _scriptedStates = new(StringComparer.OrdinalIgnoreCase);
    private int _counter;

    public IReadOnlyCollection<string> AllTokens => _grants.Keys.ToArray();

    /// <summary>Makes the next token minted for <paramref name="audience"/> come out in <paramref name="state"/>.</summary>
    public void ScriptNext(string audience, TokenState state) =>
        _scriptedStates.GetOrAdd(audience, _ => new ConcurrentQueue<TokenState>()).Enqueue(state);

    public string Mint(TestPerson person, string audience)
    {
        var state = _scriptedStates.TryGetValue(audience, out var queue) && queue.TryDequeue(out var scripted)
            ? scripted
            : TokenState.Valid;

        var token = FakeJwt.Create($"{person.Key}-{audience}-{Interlocked.Increment(ref _counter)}");
        _grants[token] = new TokenGrant(person, audience, state);
        return token;
    }

    public bool TryGet(string? token, out TokenGrant grant)
    {
        grant = null!;
        return token is not null && _grants.TryGetValue(token, out grant!);
    }

    /// <summary>The downstream's view: accept only a live token minted for this audience, otherwise fail the way the real service would.</summary>
    public TokenGrant Authorize(string token, string audience)
    {
        if (!TryGet(token, out var grant) || !string.Equals(grant.Audience, audience, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownstreamAuthenticationException(audience, 401);
        }

        return grant.State switch
        {
            TokenState.Expired => throw new DownstreamAuthenticationException(audience, 401),
            TokenState.ClaimsChallenge => throw new ClaimsChallengeException(audience, ClaimsChallenge),
            _ => grant,
        };
    }

    public ClaimsPrincipal ToPrincipal(TokenGrant grant) => new(new ClaimsIdentity(
        [
            new Claim("oid", grant.Person.ObjectId),
            new Claim("tid", grant.Person.TenantId),
            new Claim("name", grant.Person.DisplayName),
        ],
        "test"));
}

/// <summary>JWT-shaped strings, so the leak detector sees exactly what it would see for a real token.</summary>
internal static class FakeJwt
{
    public static string Create(string marker)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{marker}\"}}")}.{B64("signature-" + marker)}";
    }
}
