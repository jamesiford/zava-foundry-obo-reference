namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Audience/issuer policy for an inbound access token. Signature keys come from <see cref="Authority"/>'s OIDC
/// metadata, so rotation needs no redeploy.
/// </summary>
public sealed class JwtValidationOptions
{
    public const string SectionName = "ZavaApi:Auth";

    /// <summary>Entra authority whose <c>/.well-known/openid-configuration</c> publishes the signing keys.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Accepted <c>iss</c> values. Plural because v1 and v2 Entra tokens use different issuer formats.</summary>
    public string[] ValidIssuers { get; set; } = [];

    /// <summary>Accepted <c>aud</c> values — the MCP server's own app ID URI / client ID, never a caller's.</summary>
    public string[] ValidAudiences { get; set; } = [];

    /// <summary>Tolerated clock drift between the issuer and this host.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);
}
