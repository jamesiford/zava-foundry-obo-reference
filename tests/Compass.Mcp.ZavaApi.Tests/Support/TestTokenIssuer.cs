using System.Security.Cryptography;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Compass.Mcp.ZavaApi.Tests.Support;

/// <summary>
/// Plays the part of Entra in tests: mints RS256 tokens with a throwaway key and hands out the matching
/// validator, so the real validation code runs end to end.
/// </summary>
public sealed class TestTokenIssuer : IDisposable
{
    public const string Issuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";

    public const string Audience = "api://zava-api-mcp";

    public const string ObjectId = "5f0e7c1a-2b44-4c1e-9d6a-0d3b8a1f7e21";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;

    public TestTokenIssuer() => _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };

    public static JwtValidationOptions Options() => new()
    {
        Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
        ValidIssuers = [Issuer],
        ValidAudiences = [Audience],
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    public IAccessTokenValidator CreateValidator() => new JwtAccessTokenValidator(
        new StaticSigningKeyProvider(_key),
        Microsoft.Extensions.Options.Options.Create(Options()),
        NullLogger<JwtAccessTokenValidator>.Instance);

    public string CreateToken(
        string audience = Audience,
        string issuer = Issuer,
        TimeSpan? validFor = null,
        TimeSpan? issuedAgo = null,
        SecurityKey? signingKey = null)
    {
        var issuedAt = DateTime.UtcNow - (issuedAgo ?? TimeSpan.Zero);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + (validFor ?? TimeSpan.FromHours(1)),
            Claims = new Dictionary<string, object> { ["oid"] = ObjectId },
            SigningCredentials = new SigningCredentials(signingKey ?? _key, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>A token that expired well outside the validator's clock-skew allowance.</summary>
    public string CreateExpiredToken() => CreateToken(issuedAgo: TimeSpan.FromHours(2), validFor: TimeSpan.FromHours(1));

    /// <summary>A key the validator doesn't trust, for forged-signature cases.</summary>
    public static RsaSecurityKey CreateUntrustedKey() => new(RSA.Create(2048)) { KeyId = "test-key" };

    public void Dispose() => _rsa.Dispose();
}
