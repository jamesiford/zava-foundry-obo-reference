using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Compass.Shared.Observability.Redaction;

/// <summary>
/// Redacts JWT-shaped text from log records on their way to an exporter, so a token that slips into a message or a
/// structured property never reaches Application Insights (dev spec §13).
/// </summary>
/// <remarks>Must be registered <em>before</em> any exporter processor: processors run in registration order.</remarks>
public sealed class TokenRedactingLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        data.Body = JwtRedactor.Redact(data.Body);
        data.FormattedMessage = JwtRedactor.Redact(data.FormattedMessage);

        if (data.Attributes is { Count: > 0 } attributes)
        {
            data.Attributes = attributes
                .Select(a => a.Value is string s ? new KeyValuePair<string, object?>(a.Key, JwtRedactor.Redact(s)) : a)
                .ToList();
        }
    }
}
