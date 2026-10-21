using Microsoft.IdentityModel.Tokens;

namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Source of signature-verification keys. An abstraction so production uses Entra's rotating JWKS while tests
/// supply a fixed key without any network access.
/// </summary>
public interface ISigningKeyProvider
{
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken);
}
