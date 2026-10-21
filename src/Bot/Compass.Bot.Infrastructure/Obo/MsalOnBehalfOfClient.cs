using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace Compass.Bot.Infrastructure.Obo;

/// <summary>The real MSAL <c>AcquireTokenOnBehalfOf</c> call. Only exercised against a live tenant (see the live handoff checklist).</summary>
/// <remarks>
/// A fresh confidential-client instance is built per exchange. MSAL keeps tokens in an in-memory cache owned by the
/// application object, so a short-lived instance means nothing outlives the turn and the next turn cannot be served
/// from a previous person's cache entry (contract rule 2: request-only retention). MSAL's static authority-metadata
/// cache is shared, so the cost is small.
/// </remarks>
public sealed class MsalOnBehalfOfClient(IOptions<OnBehalfOfOptions> options, IClientAssertionProvider assertions) : IOnBehalfOfClient
{
    private readonly OnBehalfOfOptions _options = options.Value;

    public async Task<string> AcquireAccessTokenAsync(IReadOnlyCollection<string> scopes, string userAssertion, CancellationToken cancellationToken)
    {
        var app = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority($"{_options.AuthorityHost.TrimEnd('/')}/{_options.TenantId}")
            .WithClientAssertion(async ct => await assertions.GetAssertionAsync(ct))
            .Build();

        var result = await app
            .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userAssertion))
            .ExecuteAsync(cancellationToken);

        return result.AccessToken;
    }
}
