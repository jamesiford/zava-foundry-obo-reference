using Compass.Bot.Domain;

namespace Compass.Bot.Application.Abstractions;

/// <summary>Validates the Teams SSO token and turns it into the <see cref="PersonContext"/> used for the whole turn.</summary>
public interface IPersonContextResolver
{
    /// <exception cref="Exceptions.PersonResolutionException">The token is missing, expired or not issued for this bot.</exception>
    Task<PersonContext> ResolveAsync(AccessToken ssoToken, CancellationToken cancellationToken);
}
