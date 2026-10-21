namespace Compass.Shared.Identity.Tokens;

public interface IAccessTokenValidator
{
    /// <summary>Validates audience, issuer, lifetime and signature. Never throws for a bad token.</summary>
    Task<AccessTokenValidationResult> ValidateAsync(string? token, CancellationToken cancellationToken = default);
}
