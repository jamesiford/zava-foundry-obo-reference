using Compass.Bot.Application;
using Compass.Bot.Host.Readiness;
using Compass.Bot.Infrastructure;
using Compass.Bot.Orchestration;

namespace Compass.Bot.Host;

public static class BotServiceCollectionExtensions
{
    /// <summary>
    /// The bot's three layers in one call, so Program.cs has a single line to get wrong and a test can build the same
    /// composition with scope and build validation on.
    /// </summary>
    public static IServiceCollection AddCompassBot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCompassBotApplication(SharePointRouteSetting.Read(configuration), RetrievalSettingsReader.Read(configuration));
        services.AddCompassBotOrchestration();
        services.AddCompassBotInfrastructure(configuration);
        return services;
    }
}
