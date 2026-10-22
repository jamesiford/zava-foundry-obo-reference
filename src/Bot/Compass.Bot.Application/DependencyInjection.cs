using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Bot.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the sender, the logging behavior, every Bot command/query handler and the SharePoint route policy.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="route">
    /// Which SharePoint route(s) the deployment runs. Defaults to <see cref="SharePointRoute.Both"/> here (the
    /// historical behaviour of this overload); the host passes the configured value, whose own default is the knowledge base.
    /// </param>
    /// <param name="retrieval">
    /// How route A treats its hits. Defaults to <see cref="RetrievalSettings"/>'s own values (no relevance cut, no summary),
    /// the historical behaviour of this overload; the host passes the configured values.
    /// </param>
    public static IServiceCollection AddCompassBotApplication(
        this IServiceCollection services,
        SharePointRoute route = SharePointRoute.Both,
        RetrievalSettings? retrieval = null)
    {
        services.AddSingleton(new BriefingRoutePolicy(route));
        services.AddSingleton(retrieval ?? new RetrievalSettings());
        services.TryAddSingleton<IAnswerFromExtracts, UnavailableAnswerFromExtracts>();
        return services.AddMessaging(typeof(DependencyInjection).Assembly);
    }
}
