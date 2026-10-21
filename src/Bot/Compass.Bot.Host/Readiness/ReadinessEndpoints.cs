using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Host.Readiness;

public static class ReadinessEndpoints
{
    public const string Path = "/health/ready";

    /// <summary>
    /// Maps the configuration-completeness probe. It is anonymous on purpose: it returns setting names only, and it
    /// must work before any Entra or Teams wiring exists, which is exactly when it is needed.
    /// </summary>
    public static IEndpointRouteBuilder MapBotReadiness(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, (IConfiguration configuration) =>
        {
            var missing = BotConfigurationReadiness.FindMissing(configuration);
            return missing.Count == 0
                ? Results.Json(new { status = "ready" })
                : Results.Json(new { status = "incomplete", missing }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        return endpoints;
    }
}
