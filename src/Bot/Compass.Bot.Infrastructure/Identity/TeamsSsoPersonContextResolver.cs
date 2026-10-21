using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Domain;
using Compass.Shared.Identity.Tokens;

namespace Compass.Bot.Infrastructure.Identity;

/// <summary>
/// Validates the Teams SSO token's audience, issuer, lifetime and signature, then reads the person from it. This is the
/// one place a person is established for a turn (contract rule 1).
/// </summary>
public sealed class TeamsSsoPersonContextResolver(IAccessTokenValidator validator) : IPersonContextResolver
{
    public async Task<PersonContext> ResolveAsync(AccessToken ssoToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ssoToken);

        var result = await validator.ValidateAsync(ssoToken.Reveal(), cancellationToken);
        if (!result.IsValid || result.Principal is null)
        {
            throw new PersonResolutionException($"The Teams SSO token was rejected ({result.Status}).");
        }

        var oid = result.ObjectId;
        var tenant = result.Principal.FindFirst("tid")?.Value;
        if (string.IsNullOrWhiteSpace(oid) || string.IsNullOrWhiteSpace(tenant))
        {
            throw new PersonResolutionException("The Teams SSO token has no oid/tid claims.");
        }

        return new PersonContext(oid, tenant, result.Principal.FindFirst("name")?.Value);
    }
}
