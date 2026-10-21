using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Shared.Identity.Tokens;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers audience/issuer JWT validation using Entra's published signing keys. Options are validated at
    /// startup so a missing audience fails the deploy, not the first request.
    /// </summary>
    public static IServiceCollection AddJwtAccessTokenValidation(this IServiceCollection services, Action<JwtValidationOptions> configure)
    {
        services.AddOptions<JwtValidationOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Authority), "Authority is required.")
            .Validate(o => o.ValidIssuers.Length > 0, "At least one valid issuer is required.")
            .Validate(o => o.ValidAudiences.Length > 0, "At least one valid audience is required.")
            .ValidateOnStart();

        services.TryAddSingleton<ISigningKeyProvider, OpenIdConnectSigningKeyProvider>();
        services.TryAddSingleton<IAccessTokenValidator, JwtAccessTokenValidator>();
        return services;
    }
}
