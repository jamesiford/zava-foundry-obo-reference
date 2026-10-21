namespace Compass.Agents.Contracts.Knowledge;

/// <summary>
/// A grounded answer from the knowledge base plus the documents it cited. Like <c>RetrievalResult</c>, it is whatever
/// SharePoint allowed <em>this person</em> to see: an answer that says nothing was found means "nothing you may see"
/// (contract rule 4).
/// </summary>
/// <param name="Answer">The answer text with the model's inline citation markers already removed.</param>
/// <param name="Sources">The distinct documents the answer cites, in the order they were cited.</param>
public sealed record KnowledgeQueryResult(string Answer, IReadOnlyList<KnowledgeSource> Sources);

/// <summary>One cited document: a readable title and, when the knowledge base reported one, its web address.</summary>
public sealed record KnowledgeSource(string Title, string? Url = null)
{
    /// <summary>A title-only source, for results that carry no link.</summary>
    public static implicit operator KnowledgeSource(string title) => new(title);
}
