namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// The answer prompt agent ran but did not produce a reply. Carries only a status and an error code, never the prompt or an
/// extract, so it is always safe to log.
/// </summary>
public sealed class AnswerAgentException(int statusCode, string? code)
    : Exception($"The SharePoint answer agent failed (status {statusCode}, code {code ?? "unknown"}).")
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}
