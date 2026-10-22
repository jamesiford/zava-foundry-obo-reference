namespace Compass.Tools.PublishAgents;

/// <summary>Resolved inputs for one run, whether they came from arguments or environment.</summary>
/// <param name="ProjectEndpoint">Foundry project endpoint. Not needed for <see cref="WhatIf"/>.</param>
/// <param name="ZavaApiMcpEndpoint">Full MCP webhook URL of the Function App, handed to the ZavaApi agent as <c>ZavaApi__McpEndpoint</c>.</param>
/// <param name="RepositoryRoot">Repository root that <see cref="HostedAgentSpec.ProjectPath"/> is relative to.</param>
/// <param name="ManifestPath">Where the published versions are recorded.</param>
/// <param name="Only">Restrict the run to these agent names; empty means all.</param>
/// <param name="WhatIf">Plan only: no build, no network, no credential.</param>
/// <param name="Agent365Enabled">
/// Switches Agent 365 trace export on for the hosted agents by setting <c>FOUNDRY_AGENT365_TRACING_ENABLED=true</c> in their
/// environment. Off by default, in which case the variable is not set.
/// </param>
public sealed record PublishSettings(
    string? ProjectEndpoint,
    string? ZavaApiMcpEndpoint,
    string RepositoryRoot,
    string ManifestPath,
    IReadOnlySet<string> Only,
    bool WhatIf,
    bool Agent365Enabled = false);
