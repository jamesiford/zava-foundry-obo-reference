using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Fetches and caches signing keys from the authority's OIDC discovery document; the configuration manager
/// handles refresh on key rotation.
/// </summary>
public sealed class OpenIdConnectSigningKeyProvider : ISigningKeyProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public OpenIdConnectSigningKeyProvider(IOptions<JwtValidationOptions> options)
    {
        var authority = options.Value.Authority.TrimEnd('/');
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
    {
        var config = await _configuration.GetConfigurationAsync(cancellationToken);
        return config.SigningKeys.ToArray();
    }
}
