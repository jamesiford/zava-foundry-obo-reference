using Microsoft.Extensions.Time.Testing;

namespace Compass.Tools.PublishAgents.Tests;

public sealed class AgentPublisherTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<string> _log = [];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "publish-tests-" + Guid.NewGuid().ToString("N"));

    private static HostedAgentSpec Spec(string name) =>
        new(name, $"src/{name}/{name}.csproj", "0.5", "1Gi", new Dictionary<string, string>());

    private AgentPublisher Publisher(FakeService service, TimeSpan? timeout = null) =>
        new(new FakePackager(), service, _time, TimeSpan.FromSeconds(10), timeout ?? TimeSpan.FromMinutes(5), _log.Add);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Records_each_version_once_it_is_active()
    {
        var service = new FakeService { CreateStatus = "active", CreateVersion = "7" };

        var manifest = await Publisher(service).PublishAsync([Spec("A"), Spec("B")], "root", new AgentVersionManifest(), default);

        Assert.Equal("7", manifest.Versions["A"]);
        Assert.Equal("7", manifest.Versions["B"]);
        Assert.Equal(0, service.GetCalls);
    }

    [Fact]
    public async Task Polls_until_the_version_turns_active()
    {
        var service = new FakeService { CreateStatus = "creating", CreateVersion = "3", Polls = ["creating", "active"] };

        var run = Publisher(service).PublishAsync([Spec("A")], "root", new AgentVersionManifest(), default);
        while (!run.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Yield();
        }

        var manifest = await run;
        Assert.Equal("3", manifest.Versions["A"]);
        Assert.Equal(2, service.GetCalls);
    }

    [Fact]
    public async Task A_failed_version_fails_the_run_with_the_service_error()
    {
        var service = new FakeService { CreateStatus = "failed", CreateVersion = "2", CreateError = "image start crashed" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Publisher(service).PublishAsync([Spec("A")], "root", new AgentVersionManifest(), default));

        Assert.Contains("image start crashed", ex.Message);
        Assert.Contains("failed to start", ex.Message);
    }

    [Fact]
    public async Task Times_out_when_the_version_never_becomes_active()
    {
        var service = new FakeService { CreateStatus = "creating", CreateVersion = "1", Polls = ["creating", "creating", "creating", "creating"] };

        var run = Publisher(service, TimeSpan.FromSeconds(25)).PublishAsync([Spec("A")], "root", new AgentVersionManifest(), default);
        while (!run.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Yield();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Contains("still 'creating'", ex.Message);
    }

    [Fact]
    public async Task A_failure_leaves_earlier_agents_recorded_so_a_retry_can_continue()
    {
        var service = new FakeService { CreateVersion = "4", FailAgent = "B" };
        var manifest = new AgentVersionManifest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Publisher(service).PublishAsync([Spec("A"), Spec("B")], "root", manifest, default));

        Assert.Equal("4", manifest.Versions["A"]);
        Assert.False(manifest.Versions.ContainsKey("B"));
    }

    [Fact]
    public void Manifest_round_trips_and_renders_azd_and_app_setting_commands()
    {
        var path = Path.Combine(_dir, "nested", "agent-versions.json");
        var manifest = new AgentVersionManifest();
        manifest.Record(HostedAgentCatalog.SharePointRetrievalAgent, "5");
        manifest.Record(HostedAgentCatalog.ZavaApiAgent, "9");

        manifest.Save(path);
        var loaded = AgentVersionManifest.Load(path);

        Assert.Equal("9", loaded.Versions[HostedAgentCatalog.ZavaApiAgent]);
        Assert.Contains("azd env set SHAREPOINT_RETRIEVAL_AGENT_VERSION 5", loaded.ToAzdEnvCommands());
        Assert.Contains("azd env set ZAVA_API_AGENT_VERSION 9", loaded.ToAzdEnvCommands());
        Assert.Contains("Bot__Foundry__ZavaApiAgent__Version=9", loaded.ToBotAppSettings());
    }

    [Fact]
    public void Manifest_load_tolerates_a_missing_or_corrupt_file()
    {
        Directory.CreateDirectory(_dir);
        var corrupt = Path.Combine(_dir, "bad.json");
        File.WriteAllText(corrupt, "{ not json");

        Assert.Empty(AgentVersionManifest.Load(Path.Combine(_dir, "missing.json")).Versions);
        Assert.Empty(AgentVersionManifest.Load(corrupt).Versions);
    }

    [Fact]
    public void What_if_plan_describes_each_agent_and_contains_no_secrets_or_ids()
    {
        var settings = new PublishSettings(null, "https://f.example", "root", "m.json", new HashSet<string>(), WhatIf: true);

        var plan = PublishPlanFormatter.Format(HostedAgentCatalog.Create(settings), settings);

        Assert.Contains("Agent SharePointRetrievalAgent", plan);
        Assert.Contains("Agent ZavaApiAgent", plan);
        Assert.Contains("dotnet_10", plan);
        Assert.Contains("dotnet Compass.Agents.Hosted.ZavaApi.dll", plan);
        Assert.Contains("responses 2.0.0", plan);
        Assert.Contains("ZavaApi__McpEndpoint=https://f.example/runtime/webhooks/mcp", plan);
    }

    private sealed class FakePackager : IAgentPackager
    {
        public Task<string> PackageAsync(HostedAgentSpec spec, string repositoryRoot, CancellationToken cancellationToken) =>
            Task.FromResult("/tmp/" + spec.Name);
    }

    private sealed class FakeService : IAgentVersionService
    {
        private int _poll;

        public string CreateStatus { get; init; } = "active";

        public string CreateVersion { get; init; } = "1";

        public string? CreateError { get; init; }

        public string? FailAgent { get; init; }

        public string[] Polls { get; init; } = [];

        public int GetCalls { get; private set; }

        public Task<AgentVersionState> CreateFromFolderAsync(HostedAgentSpec spec, string folder, CancellationToken cancellationToken) =>
            Task.FromResult(spec.Name == FailAgent
                ? new AgentVersionState(spec.Name, CreateVersion, "failed", "boom")
                : new AgentVersionState(spec.Name, CreateVersion, CreateStatus, CreateError));

        public Task<AgentVersionState> GetAsync(string name, string version, CancellationToken cancellationToken)
        {
            GetCalls++;
            var status = _poll < Polls.Length ? Polls[_poll++] : "active";
            return Task.FromResult(new AgentVersionState(name, version, status));
        }
    }
}
