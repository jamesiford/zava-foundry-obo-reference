using System.Diagnostics;
using OpenTelemetry;

namespace Compass.Shared.Observability.Redaction;

/// <summary>
/// Redacts JWT-shaped values from span tags, the display name and the status description before export, and drops a span whose events hold one. HTTP client instrumentation and
/// custom spans are the usual way a header value ends up in a trace attribute.
/// </summary>
public sealed class TokenRedactingActivityProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        foreach (var tag in data.TagObjects.ToList())
        {
            if (tag.Value is string value && JwtRedactor.ContainsToken(value))
            {
                data.SetTag(tag.Key, JwtRedactor.Redact(value));
            }
        }

        if (JwtRedactor.ContainsToken(data.DisplayName))
        {
            data.DisplayName = JwtRedactor.Redact(data.DisplayName)!;
        }

        // Exporters (Agent 365 included) send the status description, and a failed span's description is usually an exception message.
        if (JwtRedactor.ContainsToken(data.StatusDescription))
        {
            data.SetStatus(data.Status, JwtRedactor.Redact(data.StatusDescription));
        }

        // Span events (an instrumentation's exception event carries the message and stack trace) are read-only once added, so they
        // cannot be scrubbed in place. A span whose event holds a token is dropped from every exporter instead of exported with it.
        if (EventHoldsToken(data))
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    private static bool EventHoldsToken(Activity activity)
    {
        foreach (var activityEvent in activity.Events)
        {
            if (JwtRedactor.ContainsToken(activityEvent.Name))
            {
                return true;
            }

            foreach (var tag in activityEvent.Tags)
            {
                if (tag.Value is string value && JwtRedactor.ContainsToken(value))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
