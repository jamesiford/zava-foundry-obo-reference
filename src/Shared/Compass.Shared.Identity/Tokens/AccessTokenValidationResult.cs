using System.Security.Claims;

namespace Compass.Shared.Identity.Tokens;

/// <summary>Why a token was accepted or rejected. Lets callers log and test precisely without parsing messages.</summary>
public enum TokenValidationStatus
{
    Valid,
    Missing,
    Malformed,
    Expired,
    InvalidAudience,
    InvalidIssuer,
    InvalidSignature,
}

/// <summary>Outcome of validating an access token; <see cref="Principal"/> is set only when valid.</summary>
public sealed record AccessTokenValidationResult(TokenValidationStatus Status, ClaimsPrincipal? Principal = null)
{
    public bool IsValid => Status == TokenValidationStatus.Valid;

    /// <summary>The person the token was issued for (<c>oid</c>), used for correlation — never the token itself.</summary>
    public string? ObjectId => Principal?.FindFirst("oid")?.Value;
}
