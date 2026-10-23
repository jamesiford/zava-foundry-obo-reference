namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>The seam in front of the answer prompt agent: one prompt in, the reply text out.</summary>
public interface IAnswerAgent
{
    Task<string> AnswerAsync(string prompt, CancellationToken cancellationToken);
}
