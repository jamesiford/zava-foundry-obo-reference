using Azure.AI.AgentServer.Core;
using Azure.AI.AgentServer.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Agents.Hosting;

/// <summary>
/// Hosts a Compass response handler inside an existing ASP.NET Core app using the Foundry adapter's own middleware and
/// routes. Production containers use <c>ResponsesServer.Run</c>; this exists so tests and in-process harnesses drive the
/// <em>real</em> adapter — including how it turns a thrown exception into a failed response — instead of a lookalike.
/// </summary>
public static class HostedAgentInProcessExtensions
{
    public static IServiceCollection AddHostedAgentResponses<THandler>(this IServiceCollection services)
        where THandler : ResponseHandler
    {
        services.AddAgentServerCore();
        services.AddResponsesServer();
        services.AddScoped<ResponseHandler, THandler>();
        return services;
    }

    public static WebApplication MapHostedAgentResponses(this WebApplication app)
    {
        app.UseAgentServerCore();
        app.MapResponsesServer();
        return app;
    }
}
