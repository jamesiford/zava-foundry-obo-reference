using Compass.Bot.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Bot.Orchestration;

public static class DependencyInjection
{
    /// <summary>Registers the workflow-based briefing orchestrator. Scoped, because it uses the per-turn <c>ISender</c>.</summary>
    public static IServiceCollection AddCompassBotOrchestration(this IServiceCollection services) =>
        services.AddScoped<IBriefingOrchestrator, WorkflowBriefingOrchestrator>();
}
