namespace Compass.Mcp.ZavaApi.Application.Validation;

/// <summary>
/// Validates one request type. Kept out of the handler so a handler only orchestrates (dev spec §6, SRP).
/// </summary>
public interface IRequestValidator<in TRequest>
{
    /// <summary>Returns human-readable errors; an empty list means the request is valid.</summary>
    IReadOnlyList<string> Validate(TRequest request);
}
