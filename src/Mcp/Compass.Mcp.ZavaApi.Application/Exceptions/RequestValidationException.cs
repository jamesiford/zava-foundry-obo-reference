namespace Compass.Mcp.ZavaApi.Application.Exceptions;

/// <summary>
/// The request failed input validation before reaching its handler. Thrown by the validation pipeline behavior so
/// handlers can assume well-formed input.
/// </summary>
public sealed class RequestValidationException(IReadOnlyList<string> errors)
    : Exception($"Request validation failed: {string.Join(" ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
