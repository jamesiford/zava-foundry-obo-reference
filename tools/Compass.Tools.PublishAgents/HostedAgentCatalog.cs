namespace Compass.Tools.PublishAgents;

/// <summary>The hosted agents this repository publishes, and the environment each one needs.</summary>
public static class HostedAgentCatalog
{
    public const string SharePointRetrievalAgent = "SharePointRetrievalAgent";

    public const string ZavaApiAgent = "ZavaApiAgent";

    /// <summary>Default MCP webhook path on the Function App (Azure Functions MCP extension).</summary>
    public const string McpWebhookPath = "/runtime/webhooks/mcp";

    /// <summary>Shown in a what-if plan when the endpoint is not known yet, so the plan stays readable offline.</summary>
    public const string McpEndpointPlaceholder = "<SERVICE_MCP_ENDPOINT>" + McpWebhookPath;

    public static IReadOnlyList<string> KnownNames { get; } = [SharePointRetrievalAgent, ZavaApiAgent];

    /// <summary>
    /// Builds the specs for a run. Only the ZavaApi agent needs configuration; the SharePoint agent talks to the public
    /// Graph endpoint, so its container is deliberately free of settings and secrets.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ZavaApi agent is selected, this is a real run and no MCP endpoint is known.</exception>
    public static IReadOnlyList<HostedAgentSpec> Create(PublishSettings settings)
    {
        var specs = new List<HostedAgentSpec>();

        if (Selects(settings, SharePointRetrievalAgent))
        {
            specs.Add(new HostedAgentSpec(
                SharePointRetrievalAgent,
                "src/Agents/Compass.Agents.Hosted.SharePointRetrieval/Compass.Agents.Hosted.SharePointRetrieval.csproj",
                Cpu: "0.5",
                Memory: "1Gi",
                EnvironmentVariables: new Dictionary<string, string>()));
        }

        if (Selects(settings, ZavaApiAgent))
        {
            specs.Add(new HostedAgentSpec(
                ZavaApiAgent,
                "src/Agents/Compass.Agents.Hosted.ZavaApi/Compass.Agents.Hosted.ZavaApi.csproj",
                Cpu: "0.5",
                Memory: "1Gi",
                EnvironmentVariables: new Dictionary<string, string> { ["ZavaApi__McpEndpoint"] = ResolveMcpEndpoint(settings) }));
        }

        return specs;
    }

    /// <summary>Turns the bare Function App host (the azd <c>SERVICE_MCP_ENDPOINT</c> output) into the webhook URL.</summary>
    public static string ToMcpWebhookUrl(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.EndsWith(McpWebhookPath, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + McpWebhookPath;
    }

    private static bool Selects(PublishSettings settings, string name) =>
        settings.Only.Count == 0 || settings.Only.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string ResolveMcpEndpoint(PublishSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ZavaApiMcpEndpoint))
        {
            return ToMcpWebhookUrl(settings.ZavaApiMcpEndpoint);
        }

        // Environment is immutable per version, so publishing without it would mint a version that can never work.
        return settings.WhatIf
            ? McpEndpointPlaceholder
            : throw new InvalidOperationException(
                "The ZavaApiAgent needs the Function App endpoint. Pass --mcp-endpoint or set SERVICE_MCP_ENDPOINT (from `azd env get-values`).");
    }
}
