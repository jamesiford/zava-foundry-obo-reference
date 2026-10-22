namespace Compass.Shared.Observability.Redaction;

/// <summary>
/// Stands in for an exception whose text contained a JWT-shaped value. It keeps the original type name and a redacted
/// message and stack trace, so the log still says what failed without carrying the token.
/// </summary>
public sealed class RedactedException(Exception original) : Exception(
    $"{original.GetType().FullName}: {JwtRedactor.Redact(original.Message)}")
{
    private readonly string? _stackTrace = JwtRedactor.Redact(original.StackTrace);

    public override string? StackTrace => _stackTrace;

    public override string ToString() => JwtRedactor.Redact(Message + (_stackTrace is null ? string.Empty : Environment.NewLine + _stackTrace))!;
}
