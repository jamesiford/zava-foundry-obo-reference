namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// The knowledge prompt agent ran but did not produce an answer. Carries only a status and an error code — never the
/// person's token or the prompt — so it is always safe to log (contract rule 2). The turn fails closed (rule 5).
/// </summary>
public sealed class KnowledgeAgentException(int statusCode, string? code)
    : Exception($"The SharePoint knowledge agent failed (status {statusCode}, code {code ?? "unknown"}).")
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}
