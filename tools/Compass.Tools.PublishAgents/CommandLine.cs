namespace Compass.Tools.PublishAgents;

/// <summary>Outcome of parsing the command line: either runnable settings, a request for help, or a message to show.</summary>
public sealed record CommandLineResult(PublishSettings? Settings, bool ShowHelp, string? Error)
{
    public static CommandLineResult Ok(PublishSettings settings) => new(settings, false, null);

    public static CommandLineResult Help() => new(null, true, null);

    public static CommandLineResult Fail(string error) => new(null, false, error);
}

/// <summary>
/// Arguments win over environment, environment wins over defaults. The environment names are the azd outputs
/// (<c>FOUNDRY_PROJECT_ENDPOINT</c>, <c>SERVICE_MCP_ENDPOINT</c>) so <c>azd env get-values</c> can feed the tool directly.
/// </summary>
public static class CommandLine
{
    public const string ProjectEndpointVariable = "FOUNDRY_PROJECT_ENDPOINT";

    public const string McpEndpointVariable = "SERVICE_MCP_ENDPOINT";

    public const string Usage = """
        Publish the Compass hosted agents to Foundry from source (no container image).

        Usage: dotnet run --project tools/Compass.Tools.PublishAgents -- [options]

          --what-if                 Print the plan and exit. No build, no network, no credential.
          --agent <name>            Publish only this agent (repeatable). Default: all.
          --project-endpoint <url>  Foundry project endpoint (env FOUNDRY_PROJECT_ENDPOINT).
          --mcp-endpoint <url>      Function App endpoint for the ZavaApiAgent (env SERVICE_MCP_ENDPOINT).
          --manifest <path>         Where to record versions (default infra/.generated/agent-versions.json).
          --repo-root <path>        Repository root (default: found by walking up to Compass.slnx).
          -h, --help                Show this help.
        """;

    public static CommandLineResult Parse(IReadOnlyList<string> args, Func<string, string?> environment, string startDirectory)
    {
        string? projectEndpoint = Clean(environment(ProjectEndpointVariable));
        string? mcpEndpoint = Clean(environment(McpEndpointVariable));
        string? manifest = null;
        string? repoRoot = null;
        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var whatIf = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    return CommandLineResult.Help();
                case "--what-if":
                    whatIf = true;
                    break;
                case "--agent" or "--project-endpoint" or "--mcp-endpoint" or "--manifest" or "--repo-root":
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return CommandLineResult.Fail($"{args[i]} needs a value.");
                    }

                    var value = args[++i];
                    switch (args[i - 1])
                    {
                        case "--agent":
                            only.Add(value);
                            break;
                        case "--project-endpoint":
                            projectEndpoint = value;
                            break;
                        case "--mcp-endpoint":
                            mcpEndpoint = value;
                            break;
                        case "--manifest":
                            manifest = value;
                            break;
                        default:
                            repoRoot = value;
                            break;
                    }

                    break;
                default:
                    return CommandLineResult.Fail($"Unknown argument '{args[i]}'.");
            }
        }

        var unknown = only.FirstOrDefault(name => !HostedAgentCatalog.KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        if (unknown is not null)
        {
            return CommandLineResult.Fail($"Unknown agent '{unknown}'. Known agents: {string.Join(", ", HostedAgentCatalog.KnownNames)}.");
        }

        var root = repoRoot ?? FindRepositoryRoot(startDirectory);
        if (root is null)
        {
            return CommandLineResult.Fail("Could not find the repository root (Compass.slnx). Pass --repo-root.");
        }

        if (!whatIf && projectEndpoint is null)
        {
            return CommandLineResult.Fail("A Foundry project endpoint is required. Pass --project-endpoint or set FOUNDRY_PROJECT_ENDPOINT.");
        }

        return CommandLineResult.Ok(new PublishSettings(
            projectEndpoint,
            mcpEndpoint,
            root,
            manifest ?? Path.Combine(root, "infra", ".generated", "agent-versions.json"),
            only,
            whatIf));
    }

    public static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Compass.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
