using Azure.AI.AgentServer.Core;
using Microsoft.Agents.A365.Observability.Runtime.Common;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Scopes;

namespace Compass.Agents.Hosting;

/// <summary>Who the agent is, as Agent 365 sees it. Ids only; nothing here identifies the signed-in person.</summary>
public sealed record AgentIdentity(string Name, string? AgentId, string? TenantId, string? BlueprintId, string? Version)
{
    /// <summary>Reads the identity the Foundry platform gave the container. <paramref name="fallbackName"/> is used when it has no name.</summary>
    public static AgentIdentity FromFoundry(string fallbackName) => new(
        FoundryEnvironment.AgentName ?? fallbackName,
        FoundryEnvironment.AgentInstanceClientId,
        FoundryEnvironment.AgentTenantId,
        FoundryEnvironment.AgentBlueprintClientId,
        FoundryEnvironment.AgentVersion);
}

/// <summary>
/// The only place this repository touches the Agent 365 observability types (merged into <c>Microsoft.OpenTelemetry</c>, which
/// arrives through the hosting adapter). Scopes use the <c>Agent365Sdk</c> source, which the distro listens to only when Agent 365
/// export is on, so with export off every call here is a no-op.
/// </summary>
/// <remarks>
/// Content capture is off by design: no request or response text, no tool arguments or results, and no person identifier is
/// ever passed in. A failure records the exception <em>type</em> only, because exporters send the status description and an
/// exception message can carry data. Nothing in here may throw into a request.
/// </remarks>
public static class Agent365Telemetry
{
    public const string SourceName = "Agent365Sdk";

    public static AgentSpan StartInvokeAgent(string agentName, AgentIdentity? identity = null)
    {
        try
        {
            var who = identity ?? AgentIdentity.FromFoundry(agentName);
            var baggage = new BaggageBuilder()
                .TenantId(who.TenantId)
                .AgentId(who.AgentId)
                .AgentName(who.Name)
                .Build();
            var scope = InvokeAgentScope.Start(new Request(), new InvokeAgentScopeDetails(), ToDetails(who));
            return new AgentSpan(scope, baggage);
        }
        catch
        {
            return AgentSpan.None;
        }
    }

    /// <param name="toolName">The tool or operation name. A fixed name from our code, never user input.</param>
    /// <param name="toolType">A short kind such as <c>mcp</c> or <c>graph</c>.</param>
    public static AgentSpan StartExecuteTool(string toolName, string toolType, AgentIdentity? identity = null)
    {
        try
        {
            var who = identity ?? AgentIdentity.FromFoundry(toolName);
            var scope = ExecuteToolScope.Start(new Request(), new ToolCallDetails(toolName, (string?)null, toolType: toolType), ToDetails(who));
            return new AgentSpan(scope, null);
        }
        catch
        {
            return AgentSpan.None;
        }
    }

    private static AgentDetails ToDetails(AgentIdentity who) => new(
        agentId: who.AgentId,
        agentName: who.Name,
        agentBlueprintId: who.BlueprintId,
        tenantId: who.TenantId,
        agentVersion: who.Version);
}

/// <summary>A started Agent 365 span. Dispose ends it; <see cref="Fail"/> marks it failed first.</summary>
public sealed class AgentSpan : IDisposable
{
    public static AgentSpan None { get; } = new(null, null);

    private readonly OpenTelemetryScope? _scope;
    private readonly IDisposable? _baggage;

    internal AgentSpan(OpenTelemetryScope? scope, IDisposable? baggage)
    {
        _scope = scope;
        _baggage = baggage;
    }

    /// <summary>Marks the span failed, keeping the exception type and dropping its message and stack.</summary>
    public void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            if (exception is OperationCanceledException)
            {
                _scope?.RecordCancellation();
                return;
            }

            _scope?.RecordError(new SpanFailure(exception.GetType().FullName ?? "error"));
            _scope?.SetTagMaybe("exception.type", exception.GetType().FullName);
        }
        catch
        {
            // Telemetry must never change the outcome of a request.
        }
    }

    public void Dispose()
    {
        try
        {
            _scope?.Dispose();
        }
        catch
        {
        }

        try
        {
            _baggage?.Dispose();
        }
        catch
        {
        }
    }

    /// <summary>Carries only the original exception's type name, as the status description.</summary>
    private sealed class SpanFailure(string typeName) : Exception(typeName);
}
