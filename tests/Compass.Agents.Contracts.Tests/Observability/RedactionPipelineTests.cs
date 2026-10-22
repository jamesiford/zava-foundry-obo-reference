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

    [Fact]
    public void Exporter_never_sees_a_token_in_a_logged_exception_when_redaction_is_registered_first()
    {
        var token = FakeTokens.Create("exception");
        var exceptions = new List<Exception?>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTokenRedaction();
        services.AddOpenTelemetry().WithLogging(l => l
            .AddProcessor(new SimpleLogRecordExportProcessor(new CaptureExceptionExporter(exceptions))));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("test")
            .LogError(new InvalidOperationException("Downstream rejected " + token), "Call failed");

        var exported = Assert.Single(exceptions);
        Assert.NotNull(exported);
        Assert.DoesNotContain(token, exported.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, exported.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, exported.Message);
    }

    [Fact]
    public void A_span_whose_exception_event_holds_a_token_is_not_exported()
    {
        var token = FakeTokens.Create("event-first");
        var exported = new List<string>();
        var spans = new List<string>();

        using (BuildTracing(redaction: true, exported, spans, batch: false, "ev-drop"))
        using (var source = new ActivitySource("ev-drop"))
        using (var activity = source.StartActivity("call"))
        {
            activity!.AddException(new InvalidOperationException("Downstream rejected " + token));
        }

        Assert.Empty(spans);
        Assert.DoesNotContain(exported, e => e.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void Control_the_same_event_does_reach_an_exporter_when_no_redaction_is_registered()
    {
        var token = FakeTokens.Create("event-control");
        var exported = new List<string>();
        var spans = new List<string>();

        using (BuildTracing(redaction: false, exported, spans, batch: false, "ev-control"))
        using (var source = new ActivitySource("ev-control"))
        using (var activity = source.StartActivity("call"))
        {
            activity!.AddException(new InvalidOperationException("Downstream rejected " + token));
        }

        Assert.Single(spans);
        Assert.Contains(exported, e => e.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void A_span_with_an_ordinary_exception_event_is_still_exported()
    {
        var exported = new List<string>();
        var spans = new List<string>();

        using (BuildTracing(redaction: true, exported, spans, batch: false, "ev-ordinary"))
        using (var source = new ActivitySource("ev-ordinary"))
        using (var activity = source.StartActivity("call"))
        {
            activity!.AddException(new InvalidOperationException("Downstream returned 503"));
        }

        Assert.Single(spans);
        Assert.Contains(exported, e => e.Contains("Downstream returned 503", StringComparison.Ordinal));
    }

    [Fact]
    public void The_batch_export_processor_drops_the_span_too_because_real_exporters_batch()
    {
        var token = FakeTokens.Create("event-batch");
        var exported = new List<string>();
        var spans = new List<string>();

        using (var provider = BuildTracing(redaction: true, exported, spans, batch: true, "ev-batch"))
        {
            using (var source = new ActivitySource("ev-batch"))
            using (var activity = source.StartActivity("call"))
            {
                activity!.AddException(new InvalidOperationException("Downstream rejected " + token));
            }

            provider.GetRequiredService<TracerProvider>().ForceFlush();
        }

        Assert.Empty(spans);
        Assert.DoesNotContain(exported, e => e.Contains(token, StringComparison.Ordinal));
    }

    private static ServiceProvider BuildTracing(bool redaction, List<string> exported, List<string> spans, bool batch, string sourceName)
    {
        var services = new ServiceCollection();
        if (redaction)
        {
            services.AddTokenRedaction();
        }

        services.AddOpenTelemetry().WithTracing(t =>
        {
            t.AddSource(sourceName);
            var exporter = new CaptureEventExporter(exported, spans);
            t.AddProcessor(batch ? new BatchActivityExportProcessor(exporter) : new SimpleActivityExportProcessor(exporter));
        });

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TracerProvider>();
        return provider;
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

    private sealed class CaptureExceptionExporter(List<Exception?> sink) : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                sink.Add(record.Exception);
            }

            return ExportResult.Success;
        }
    }

    private sealed class CaptureEventExporter(List<string> sink, List<string> spans) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                spans.Add(activity.DisplayName);
                foreach (var activityEvent in activity.Events)
                {
                    sink.Add(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                    {
                        sink.Add($"{tag.Key}={tag.Value}");
                    }
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
