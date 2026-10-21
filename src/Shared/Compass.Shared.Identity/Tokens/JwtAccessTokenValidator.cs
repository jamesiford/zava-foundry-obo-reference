using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Validates the <c>x-client-zava-api-token</c> (dev spec §9, hop 6). Audience and issuer are the trust boundary:
/// a token minted for any other API must not be replayable against this server.
/// </summary>
public sealed class JwtAccessTokenValidator(
    ISigningKeyProvider signingKeys,
    IOptions<JwtValidationOptions> options,
    ILogger<JwtAccessTokenValidator> logger) : IAccessTokenValidator
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<AccessTokenValidationResult> ValidateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AccessTokenValidationResult(TokenValidationStatus.Missing);
        }

        var settings = options.Value;
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = settings.ValidIssuers,
            ValidateAudience = true,
            ValidAudiences = settings.ValidAudiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = settings.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = await signingKeys.GetSigningKeysAsync(cancellationToken),
        };

        var result = await Handler.ValidateTokenAsync(token, parameters);
        if (result.IsValid)
        {
            return new AccessTokenValidationResult(TokenValidationStatus.Valid, new ClaimsPrincipal(result.ClaimsIdentity));
        }

        var status = Classify(result.Exception);

        // Exception messages from the token library can echo claim values; log the category only.
        logger.LogWarning("Access token rejected: {Status} ({Reason})", status, result.Exception?.GetType().Name);
        return new AccessTokenValidationResult(status);
    }

    private static TokenValidationStatus Classify(Exception? exception) => exception switch
    {
        SecurityTokenExpiredException or SecurityTokenNotYetValidException or SecurityTokenNoExpirationException
            or SecurityTokenInvalidLifetimeException => TokenValidationStatus.Expired,
        SecurityTokenInvalidAudienceException => TokenValidationStatus.InvalidAudience,
        SecurityTokenInvalidIssuerException => TokenValidationStatus.InvalidIssuer,
        SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException
            or SecurityTokenInvalidAlgorithmException => TokenValidationStatus.InvalidSignature,
        _ => TokenValidationStatus.Malformed,
    };
}
