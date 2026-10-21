using Compass.Shared.Observability.Redaction;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Compass.Shared.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Adds the token-redacting processors for logs and traces. Call it before adding any exporter so redaction runs
    /// first; every host (bot, MCP, hosted agents) calls this once so there is exactly one place the rule lives.
    /// </summary>
    public static IServiceCollection AddTokenRedaction(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithLogging(logging => logging.AddProcessor<TokenRedactingLogProcessor>())
            .WithTracing(tracing => tracing.AddProcessor<TokenRedactingActivityProcessor>());
        return services;
    }
}
