using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Compass.Bot.Infrastructure.Obo;

/// <summary>
/// Gets a managed-identity token for the token-exchange audience, which Entra accepts as the client assertion because
/// the bot's app registration carries a federated credential trusting that identity (dev spec D8, §9 hop 2).
/// </summary>
public sealed class ManagedIdentityClientAssertionProvider(IOptions<OnBehalfOfOptions> options) : IClientAssertionProvider
{
    private readonly OnBehalfOfOptions _options = options.Value;

    public async Task<string> GetAssertionAsync(CancellationToken cancellationToken)
    {
        var credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_options.ManagedIdentityClientId));
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([$"{_options.FederatedCredentialAudience}/.default"]),
            cancellationToken);
        return token.Token;
    }
}
