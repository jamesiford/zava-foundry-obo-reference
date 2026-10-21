using Compass.Shared.Identity.Tokens;
using Microsoft.IdentityModel.Tokens;

namespace Compass.Mcp.ZavaApi.Tests.Support;

/// <summary>Serves a fixed key so token validation tests never touch the network or Entra.</summary>
public sealed class StaticSigningKeyProvider(params SecurityKey[] keys) : ISigningKeyProvider
{
    public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<SecurityKey>>(keys);
}
