namespace Compass.Mcp.ZavaApi.Application.Validation;

/// <summary>
/// Shared shape check for member and account numbers, defined once so the three queries can't drift apart (DRY).
/// </summary>
internal static class IdentifierRules
{
    public const int MaxLength = 32;

    public static IEnumerable<string> Check(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return $"{name} is required.";
        }
        else if (value.Length > MaxLength)
        {
            yield return $"{name} must be at most {MaxLength} characters.";
        }
    }
}
