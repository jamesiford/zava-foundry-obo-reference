namespace Compass.Tools.PublishAgents;

/// <summary>
/// Drives package, create-version, wait-until-active for each agent. The only class that knows the order of those steps,
/// so Foundry and <c>dotnet</c> are replaceable fakes in tests.
/// </summary>
public sealed class AgentPublisher(
    IAgentPackager packager,
    IAgentVersionService service,
    TimeProvider timeProvider,
    TimeSpan pollInterval,
    TimeSpan timeout,
    Action<string> log)
{
    /// <exception cref="InvalidOperationException">A version reported failed or did not become active in time.</exception>
    public async Task<AgentVersionManifest> PublishAsync(
        IReadOnlyList<HostedAgentSpec> specs,
        string repositoryRoot,
        AgentVersionManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var spec in specs)
        {
            log($"[{spec.Name}] publishing {spec.ProjectPath}");
            var folder = await packager.PackageAsync(spec, repositoryRoot, cancellationToken);

            log($"[{spec.Name}] creating version from {folder}");
            var created = await service.CreateFromFolderAsync(spec, folder, cancellationToken);
            var active = await WaitUntilActiveAsync(created, cancellationToken);

            manifest.Record(active.Name, active.Version);
            log($"[{spec.Name}] version {active.Version} is active");
        }

        return manifest;
    }

    private async Task<AgentVersionState> WaitUntilActiveAsync(AgentVersionState state, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + timeout;

        while (true)
        {
            if (string.Equals(state.Status, AgentVersionState.Active, StringComparison.OrdinalIgnoreCase))
            {
                return state;
            }

            if (string.Equals(state.Status, AgentVersionState.Failed, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Agent '{state.Name}' version {state.Version} failed to start: {state.Error ?? "no error detail returned"}");
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                throw new InvalidOperationException(
                    $"Agent '{state.Name}' version {state.Version} was still '{state.Status}' after {timeout.TotalMinutes:0.#} minutes.");
            }

            log($"[{state.Name}] version {state.Version} is {state.Status}; waiting");
            await Task.Delay(pollInterval, timeProvider, cancellationToken);
            state = await service.GetAsync(state.Name, state.Version, cancellationToken);
        }
    }
}
