using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace Compass.Bot.Infrastructure.Obo;

/// <summary>
/// Implements <see cref="IObOTokenBroker"/>: picks the scopes for the audience, performs the exchange and translates
/// MSAL failures into the application's typed <see cref="TokenAcquisitionException"/>.
/// </summary>
public sealed class OboTokenBroker(IOnBehalfOfClient client, IOptions<OnBehalfOfOptions> options) : IObOTokenBroker
{
    private readonly OnBehalfOfOptions _options = options.Value;

    public async Task<AccessToken> AcquireAsync(AccessToken userAssertion, DownstreamAudience audience, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userAssertion);

        var scopes = audience switch
        {
            DownstreamAudience.Graph => _options.GraphScopes,
            DownstreamAudience.ZavaApi => _options.ZavaApiScopes,
            DownstreamAudience.Search => _options.SearchScopes,
            _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, "Unknown downstream audience."),
        };

        if (scopes.Length == 0)
        {
            throw new TokenAcquisitionException(audience, $"No scopes are configured for {audience}.");
        }

        try
        {
            return new AccessToken(await client.AcquireAccessTokenAsync(scopes, userAssertion.Reveal(), cancellationToken));
        }
        catch (MsalUiRequiredException ex)
        {
            // Consent or step-up (claims challenge) needed: the host has to send the person back through sign-in.
            throw new TokenAcquisitionException(audience, $"Interaction is required to obtain a token for {audience}.", ex.Claims, ex);
        }
        catch (MsalException ex)
        {
            throw new TokenAcquisitionException(audience, $"Token exchange for {audience} failed ({ex.ErrorCode}).", inner: ex);
        }
    }
}
