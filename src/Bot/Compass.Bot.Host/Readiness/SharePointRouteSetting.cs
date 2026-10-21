using Compass.Bot.Application.Models;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Host.Readiness;

/// <summary>Reads and validates <c>Bot:SharePoint:Route</c> in one place so startup and readiness can't disagree.</summary>
public static class SharePointRouteSetting
{
    public const string Key = "Bot:SharePoint:Route";

    /// <summary>The readiness label for a bad value; names the key and what is accepted, never the value itself.</summary>
    public const string InvalidLabel = $"{Key} (must be KnowledgeBase, Retrieval or Both)";

    /// <summary>Returns the configured route (default <see cref="SharePointRoute.KnowledgeBase"/>) or null if it isn't valid.</summary>
    public static SharePointRoute? TryRead(IConfiguration configuration) =>
        BriefingRoutePolicy.TryParse(configuration[Key], out var route) ? route : null;

    /// <summary>Reads the route at startup; an unknown value fails fast rather than silently running another route.</summary>
    public static SharePointRoute Read(IConfiguration configuration) =>
        TryRead(configuration) ?? throw new InvalidOperationException($"{InvalidLabel}.");
}
