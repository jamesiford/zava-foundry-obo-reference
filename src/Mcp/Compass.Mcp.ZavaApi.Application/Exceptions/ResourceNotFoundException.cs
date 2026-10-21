namespace Compass.Mcp.ZavaApi.Application.Exceptions;

/// <summary>
/// The requested member or account doesn't exist. A typed exception lets the Host report a clean "not found" to the
/// tool caller instead of leaking a null-reference or a generic failure.
/// </summary>
public abstract class ResourceNotFoundException(string code, string message) : Exception(message)
{
    /// <summary>Stable, machine-readable code the model/tool caller can branch on.</summary>
    public string Code { get; } = code;
}
