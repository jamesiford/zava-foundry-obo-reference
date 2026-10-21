using System.Diagnostics;
using OpenTelemetry;

namespace Compass.Shared.Observability.Redaction;

/// <summary>
/// Redacts JWT-shaped values from span tags and the display name before export. HTTP client instrumentation and
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
    }
}
