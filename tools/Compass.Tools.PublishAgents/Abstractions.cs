namespace Compass.Tools.PublishAgents;

/// <summary>Builds a publishable folder (the <c>dotnet publish</c> output) for one agent.</summary>
public interface IAgentPackager
{
    /// <returns>The folder whose contents become the root of the uploaded zip.</returns>
    Task<string> PackageAsync(HostedAgentSpec spec, string repositoryRoot, CancellationToken cancellationToken);
}

/// <summary>A version as the service reports it. <see cref="Error"/> is only meaningful when the status is <c>failed</c>.</summary>
public sealed record AgentVersionState(string Name, string Version, string Status, string? Error = null)
{
    public const string Active = "active";

    public const string Failed = "failed";
}

/// <summary>The slice of the Foundry agent API this tool needs; exists so the publish flow is testable without a tenant.</summary>
public interface IAgentVersionService
{
    Task<AgentVersionState> CreateFromFolderAsync(HostedAgentSpec spec, string folder, CancellationToken cancellationToken);

    Task<AgentVersionState> GetAsync(string name, string version, CancellationToken cancellationToken);
}
