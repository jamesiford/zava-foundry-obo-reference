using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Agents.Hosting;

/// <summary>
/// Switch and start-up check for exporting hosted-agent traces to Microsoft Agent 365.
/// </summary>
/// <remarks>
/// The Foundry hosting library already builds the Microsoft OpenTelemetry distro (<c>UseMicrosoftOpenTelemetry</c>) and
/// owns the Agent 365 exporter, token acquisition and the Application Insights exporter. The distro refuses a second
/// call, so this repository does not make one. Export is controlled by the same variables the library reads;
/// this class only refuses to start when export is switched on but cannot work, so a misconfiguration is a clear
/// start-up error instead of silently exporting nothing. Only variable names are ever put in the message.
/// </remarks>
public static class Agent365Tracing
{
    /// <summary>The library's own switch. Anything other than <c>true</c> leaves Agent 365 export off.</summary>
    public const string EnabledVariable = "FOUNDRY_AGENT365_TRACING_ENABLED";

    /// <summary>Set by the Foundry platform inside a hosted container; the library exports nothing without it.</summary>
    public const string HostingEnvironmentVariable = "FOUNDRY_HOSTING_ENVIRONMENT";

    /// <summary>The agent identity's client id. Agent 365 requires it to equal the span's agent id and the token's app id.</summary>
    public const string InstanceClientIdVariable = "FOUNDRY_AGENT_INSTANCE_CLIENT_ID";

    public const string TenantIdVariable = "FOUNDRY_AGENT_TENANT_ID";

    public static IReadOnlyList<string> RequiredVariables { get; } =
        [HostingEnvironmentVariable, InstanceClientIdVariable, TenantIdVariable];

    public static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(configuration[EnabledVariable], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The names of the settings export needs that are absent. Empty when export is off.</summary>
    public static IReadOnlyList<string> MissingSettings(IConfiguration configuration) =>
        !IsEnabled(configuration)
            ? []
            : RequiredVariables.Where(name => string.IsNullOrWhiteSpace(configuration[name])).ToList();

    /// <summary>
    /// Fails fast when Agent 365 export is on but a required setting is missing. Does nothing when export is off, which is
    /// the default. Call it right after <c>AddTokenRedaction()</c> so the redactors stay ahead of every exporter.
    /// </summary>
    /// <exception cref="InvalidOperationException">Export is on and a required setting is missing (names only).</exception>
    public static IServiceCollection ValidateAgent365Tracing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var missing = MissingSettings(configuration);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Agent 365 tracing is on ({EnabledVariable}=true) but these settings are missing: {string.Join(", ", missing)}. " +
                $"Set them, or turn export off by removing {EnabledVariable}.");
        }

        return services;
    }
}
