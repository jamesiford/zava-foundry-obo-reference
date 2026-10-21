using Compass.Bot.Application.Models;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

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
    public static IServiceCollection AddCompassBotApplication(this IServiceCollection services, SharePointRoute route = SharePointRoute.Both)
    {
        services.AddSingleton(new BriefingRoutePolicy(route));
        return services.AddMessaging(typeof(DependencyInjection).Assembly);
    }
}
