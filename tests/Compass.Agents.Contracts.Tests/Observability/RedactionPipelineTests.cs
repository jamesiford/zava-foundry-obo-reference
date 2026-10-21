using System.Diagnostics;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Compass.Agents.Contracts.Tests.Observability;

/// <summary>
/// Drives the real OpenTelemetry pipeline (not just the processors in isolation) to prove what an exporter receives.
/// Registration order is the thing under test: a processor registered after the exporter never sees the data first.
/// </summary>
public sealed class RedactionPipelineTests
{
    [Fact]
    public void Exporter_never_sees_a_token_when_redaction_is_registered_first()
    {
        var token = FakeTokens.Create("log-first");
        var exported = new List<string>();

        using var provider = BuildLogging(redactionFirst: true, exported);
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("test")
            .LogInformation("Downstream call failed with {Header}", $"Bearer {token}");

        Assert.NotEmpty(exported);
        Assert.DoesNotContain(exported, e => e.Contains(token, StringComparison.Ordinal));
        Assert.Contains(exported, e => e.Contains("[REDACTED-TOKEN]", StringComparison.Ordinal));
    }

    [Fact]
    public void Redaction_registered_after_an_exporter_is_too_late_which_is_why_hosts_register_it_first()
    {
        var token = FakeTokens.Create("late");
        var exported = new List<string>();

        using var provider = BuildLogging(redactionFirst: false, exported);
        provider.GetRequiredService<ILoggerFactory>().CreateLogger("test")
            .LogInformation("Downstream call failed with {Header}", $"Bearer {token}");

        Assert.Contains(exported, e => e.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void Exporter_never_sees_a_token_in_a_span_when_redaction_is_registered_first()
    {
        var token = FakeTokens.Create("span-first");
        var exported = new List<string>();
        const string sourceName = "Compass.Tests.Redaction";

        var services = new ServiceCollection();
        services.AddTokenRedaction();
        services.AddOpenTelemetry().WithTracing(t => t
            .AddSource(sourceName)
            .AddProcessor(new SimpleActivityExportProcessor(new CaptureActivityExporter(exported))));
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();

        using (var source = new ActivitySource(sourceName))
        using (var activity = source.StartActivity("call"))
        {
            activity!.SetTag("http.request.header.x-client-zava-api-token", token);
        }

        Assert.NotEmpty(exported);
        Assert.DoesNotContain(exported, e => e.Contains(token, StringComparison.Ordinal));
    }

    private static ServiceProvider BuildLogging(bool redactionFirst, List<string> exported)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (redactionFirst)
        {
            services.AddTokenRedaction();
        }

        services.AddOpenTelemetry().WithLogging(l => l
            .AddProcessor(new SimpleLogRecordExportProcessor(new CaptureLogExporter(exported))));

        if (!redactionFirst)
        {
            services.AddTokenRedaction();
        }

        return services.BuildServiceProvider();
    }

    private sealed class CaptureLogExporter(List<string> sink) : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                sink.Add(record.FormattedMessage ?? string.Empty);
                sink.Add(record.Body ?? string.Empty);
                foreach (var attribute in record.Attributes ?? [])
                {
                    sink.Add($"{attribute.Key}={attribute.Value}");
                }
            }

            return ExportResult.Success;
        }
    }

    private sealed class CaptureActivityExporter(List<string> sink) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                sink.Add(activity.DisplayName);
                foreach (var tag in activity.TagObjects)
                {
                    sink.Add($"{tag.Key}={tag.Value}");
                }
            }

            return ExportResult.Success;
        }
    }
}
