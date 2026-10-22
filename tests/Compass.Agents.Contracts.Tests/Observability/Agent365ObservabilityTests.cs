using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Agents.Hosting;
using Compass.Shared.Observability;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Processors;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Compass.Agents.Contracts.Tests.Observability;

/// <summary>
/// Offline proof of the Agent 365 export path. A fake exporter stands in for the Agent 365 exporter and is registered the way
/// the distro registers it: after our redactors, behind the distro's own baggage-to-tag processor, on the <c>Agent365Sdk</c> source.
/// Spans are matched by a per-test agent id because activity sources are process-wide.
/// </summary>
public sealed class Agent365ObservabilityTests
{
    private static readonly string[] ContentAndPersonKeys =
    [
        "gen_ai.input.messages", "gen_ai.output.messages", "gen_ai.tool.arguments", "gen_ai.tool.call.result",
        "gen_ai.tool.description", "user.id", "user.email", "user.name", "microsoft.agent.user.id", "microsoft.agent.user.email",
        "client.address",
    ];

    private static AgentIdentity Identity(string agentId, string name = "ZavaApiAgent") =>
        new(name, agentId, "00000000-0000-0000-0000-0000000000aa", "blueprint-id", "7");

    [Fact]
    public void Invoke_agent_span_carries_identity_and_no_content_or_person()
    {
        var id = Guid.NewGuid().ToString();
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: true);

        using (var span = Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", Identity(id)))
        {
            span.Fail(new InvalidOperationException("not recorded"));
        }

        var activity = Assert.Single(Mine(exported, id));
        Assert.Equal("invoke_agent", Tag(activity, "gen_ai.operation.name"));
        Assert.Equal(id, Tag(activity, "gen_ai.agent.id"));
        Assert.Equal("00000000-0000-0000-0000-0000000000aa", Tag(activity, "microsoft.tenant.id"));
        Assert.All(ContentAndPersonKeys, key => Assert.Null(Tag(activity, key)));
    }

    [Fact]
    public void Execute_tool_span_records_the_tool_name_but_no_arguments_or_result()
    {
        var id = Guid.NewGuid().ToString();
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: true);

        using (Agent365Telemetry.StartExecuteTool("get_member_summary", "mcp", Identity(id)))
        {
        }

        var activity = Assert.Single(Mine(exported, id));
        Assert.Equal("execute_tool", Tag(activity, "gen_ai.operation.name"));
        Assert.Equal("get_member_summary", Tag(activity, "gen_ai.tool.name"));
        Assert.All(ContentAndPersonKeys, key => Assert.Null(Tag(activity, key)));
    }

    [Fact]
    public void A_failed_span_keeps_the_exception_type_and_drops_the_message()
    {
        var id = Guid.NewGuid().ToString();
        var token = FakeTokens.Create("failure");
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: true);

        using (var span = Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", Identity(id)))
        {
            span.Fail(new InvalidOperationException($"member 12345 balance 99 {token}"));
        }

        var activity = Assert.Single(Mine(exported, id));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, Tag(activity, "exception.type"));
        Assert.DoesNotContain("12345", activity.StatusDescription);
        Assert.DoesNotContain(token, Everything(activity));
    }

    [Fact]
    public void Exporter_never_sees_a_token_in_any_field_when_the_redactors_are_registered_first()
    {
        var id = Guid.NewGuid().ToString();
        var token = FakeTokens.Create("agent365");
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: true);

        // The token enters three ways: an agent name (becomes a tag and baggage, which the distro copies to tags),
        // a raw tag, and a raw status description.
        using (Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", Identity(id, name: $"agent {token}")))
        {
        }

        using (var source = new ActivitySource(Agent365Telemetry.SourceName))
        using (var raw = source.StartActivity("raw", ActivityKind.Client))
        {
            raw!.SetTag("gen_ai.agent.id", id);
            raw.SetTag("some.header", token);
            raw.SetStatus(ActivityStatusCode.Error, token);
        }

        var mine = Mine(exported, id);
        Assert.Equal(2, mine.Count);
        Assert.All(mine, a => Assert.DoesNotContain(token, Everything(a)));
        Assert.Contains(mine, a => Everything(a).Contains("[REDACTED-TOKEN]", StringComparison.Ordinal));
    }

    [Fact]
    public void Control_a_token_does_reach_an_exporter_registered_before_the_redactors()
    {
        var id = Guid.NewGuid().ToString();
        var token = FakeTokens.Create("control");
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: true, redactionFirst: false);

        using (var source = new ActivitySource(Agent365Telemetry.SourceName))
        using (var raw = source.StartActivity("raw"))
        {
            raw!.SetTag("gen_ai.agent.id", id);
            raw.SetTag("some.header", token);
        }

        Assert.Contains(token, Everything(Assert.Single(Mine(exported, id))));
    }

    [Fact]
    public void Nothing_is_exported_when_agent365_is_off_which_is_the_default()
    {
        var id = Guid.NewGuid().ToString();
        var exported = new List<Activity>();
        using var provider = BuildPipeline(exported, agent365On: false);

        using (var span = Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", Identity(id)))
        {
            span.Fail(new InvalidOperationException("ignored"));
        }

        Assert.Empty(Mine(exported, id));
    }

    [Fact]
    public void An_exporter_that_throws_never_reaches_the_caller()
    {
        var id = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddTokenRedaction();
        services.AddOpenTelemetry().WithTracing(t => t
            .AddSource(Agent365Telemetry.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(new ThrowingExporter())));
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();

        var ran = false;
        using (var span = Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", Identity(id)))
        {
            ran = true;
            span.Fail(new InvalidOperationException("x"));
        }

        Assert.True(ran);
    }

    [Fact]
    public void A_scope_that_cannot_start_returns_a_no_op_instead_of_throwing()
    {
        using var span = Agent365Telemetry.StartInvokeAgent("ZavaApiAgent", new AgentIdentity(null!, null, null, null, null));

        span.Fail(new InvalidOperationException("x"));
    }

    private static ServiceProvider BuildPipeline(List<Activity> exported, bool agent365On, bool redactionFirst = true)
    {
        var services = new ServiceCollection();
        if (redactionFirst)
        {
            services.AddTokenRedaction();
        }

        services.AddOpenTelemetry().WithTracing(tracing =>
        {
            if (agent365On)
            {
                // What UseMicrosoftOpenTelemetry does when Agent 365 export is on: listen to the source and copy baggage to tags.
                tracing.AddSource(Agent365Telemetry.SourceName).AddProcessor(new ActivityProcessor());
            }

            tracing.AddProcessor(new SimpleActivityExportProcessor(new CaptureExporter(exported)));
        });

        if (!redactionFirst)
        {
            services.AddTokenRedaction();
        }

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();
        return provider;
    }

    private static List<Activity> Mine(List<Activity> exported, string agentId) =>
        exported.Where(a => Tag(a, "gen_ai.agent.id") == agentId).ToList();

    private static string? Tag(Activity activity, string key) => activity.GetTagItem(key)?.ToString();

    // The content as it was at export time; a processor registered later can still mutate the live Activity.
    private static readonly ConditionalWeakTable<Activity, string> Snapshots = new();

    private static string Everything(Activity activity) =>
        Snapshots.TryGetValue(activity, out var snapshot) ? snapshot : Render(activity);

    private static string Render(Activity activity) => string.Join(
        '\n',
        new[] { activity.DisplayName, activity.StatusDescription ?? string.Empty }
            .Concat(activity.TagObjects.Select(t => $"{t.Key}={t.Value}"))
            .Concat(activity.Baggage.Select(b => $"{b.Key}={b.Value}"))
            .Concat(activity.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}"))));

    private sealed class CaptureExporter(List<Activity> sink) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                Snapshots.AddOrUpdate(activity, Render(activity));
                sink.Add(activity);
            }

            return ExportResult.Success;
        }
    }

    private sealed class ThrowingExporter : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch) => throw new InvalidOperationException("exporter is down");
    }
}
