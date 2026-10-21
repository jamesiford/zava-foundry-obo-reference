using System.Diagnostics;
using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>The Agent Framework workflow emits spans only when telemetry is switched on for it, and must not record message content.</summary>
public sealed class WorkflowTelemetryTests
{
    private const string WorkflowSource = "Microsoft.Agents.AI.Workflows";

    [Fact]
    public async Task Fake_TheBriefingWorkflowEmitsSpansWithoutRecordingContent()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (spans) { spans.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        await using var backend = await FakeBackend.StartAsync();
        await backend.AskAsync(TestPerson.UserA, "anything");

        Activity[] seen;
        lock (spans) { seen = [.. spans]; }

        Assert.Contains(seen, a => a.OperationName.StartsWith("executor.process", StringComparison.Ordinal));
        Assert.Contains(seen, a => a.OperationName.StartsWith("workflow", StringComparison.Ordinal));
        Assert.All(seen, a =>
        {
            Assert.DoesNotContain(a.TagObjects, t => t.Key is "executor.input" or "executor.output" or "message.content");
        });
    }
}