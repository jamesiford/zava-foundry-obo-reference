using Compass.Bot.Infrastructure.Obo;
using Microsoft.Extensions.Options;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>
/// <c>GET /api/config</c>: what the page needs to start an MSAL sign-in. All of it is public by design (a client ID, a tenant
/// ID and a scope name appear in every sign-in URL), so the page ships no configuration of its own and nothing is baked into
/// the HTML.
/// </summary>
public static class FrontDoorConfig
{
    public static IResult Handle(IOptions<OnBehalfOfOptions> options)
    {
        var obo = options.Value;
        if (!IsConfigured(obo.ClientId) || !IsConfigured(obo.TenantId))
        {
            return Results.Json(new { error = "not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var authority = $"{obo.AuthorityHost.TrimEnd('/')}/{obo.TenantId}";
        return Results.Json(new
        {
            clientId = obo.ClientId,
            tenantId = obo.TenantId,
            authority,
            // The bot's own delegated scope, the same one Teams SSO requests, so the token passes the same validator.
            scope = $"api://botid-{obo.ClientId}/access_as_user",
        });
    }

    // An unset value or an unreplaced "<placeholder>" from appsettings.json is not usable.
    private static bool IsConfigured(string value) => !string.IsNullOrWhiteSpace(value) && !value.Contains('<');
}
