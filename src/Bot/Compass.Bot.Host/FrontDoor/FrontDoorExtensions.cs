using Microsoft.AspNetCore.Builder;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>Registers and maps the web front door; both are no-ops when <c>Bot:FrontDoor:Enabled</c> is false.</summary>
public static class FrontDoorExtensions
{
    public static bool IsFrontDoorEnabled(this IConfiguration configuration) =>
        configuration.GetSection(FrontDoorOptions.SectionName).Get<FrontDoorOptions>()?.Enabled ?? new FrontDoorOptions().Enabled;

    public static IServiceCollection AddFrontDoor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FrontDoorOptions>(configuration.GetSection(FrontDoorOptions.SectionName));
        services.AddScoped<BriefingHandler>();
        return services;
    }

    /// <summary>Static files must precede <c>UseAgents</c> so the page is served without the agent pipeline's authorization.</summary>
    public static WebApplication UseFrontDoorPage(this WebApplication app)
    {
        if (app.Configuration.IsFrontDoorEnabled())
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context => ApplySecurityHeaders(context.Context.Response),
            });
        }

        return app;
    }

    /// <summary>
    /// A page that holds a bearer token in memory should run only its own scripts: no inline script, no third-party origin
    /// except Microsoft's sign-in endpoint, and no framing.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self' https://login.microsoftonline.com; " +
        "img-src 'self' data:; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        // Revalidate on every load: a deployed script change reaches people at once instead of after a heuristic cache window.
        response.Headers.CacheControl = "no-cache";
    }

    public static WebApplication MapFrontDoor(this WebApplication app)
    {
        if (!app.Configuration.IsFrontDoorEnabled())
        {
            return app;
        }

        app.MapGet("/api/config", FrontDoorConfig.Handle).AllowAnonymous();
        // Anonymous at the framework level on purpose: the bearer token is validated inside the briefing query, by the
        // same validator and OBO broker Teams uses, so there is a single place identity is decided.
        app.MapPost("/api/briefing", (HttpContext context, BriefingHandler handler) => handler.HandleAsync(context)).AllowAnonymous();
        return app;
    }
}
