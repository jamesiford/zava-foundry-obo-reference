using Compass.Agents.Contracts.Knowledge;

namespace Compass.Bot.Application.Models;

/// <summary>The "did it answer?" rule shared by the knowledge-base route and the Graph retrieval answer.</summary>
public static class KnowledgeAnswers
{
    /// <summary>
    /// The agents are told to answer exactly "I don't know" when nothing visible to the person answers the question; that
    /// and an empty answer both mean "no answer".
    /// </summary>
    public static bool HasAnswer(KnowledgeQueryResult? result)
    {
        var answer = result?.Answer?.Trim().TrimEnd('.', '!') ?? string.Empty;
        return answer.Length > 0
            && !answer.Equals("I don't know", StringComparison.OrdinalIgnoreCase)
            && !answer.Equals("I don’t know", StringComparison.OrdinalIgnoreCase)
            && !answer.Equals("I do not know", StringComparison.OrdinalIgnoreCase);
    }
}
