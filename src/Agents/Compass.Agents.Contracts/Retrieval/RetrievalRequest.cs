namespace Compass.Agents.Contracts.Retrieval;

/// <summary>Ask the SharePoint retrieval agent for content relevant to <paramref name="Query"/>.</summary>
public sealed record RetrievalRequest(string Query, int MaxResults = RetrievalRequest.DefaultMaxResults)
{
    public const int DefaultMaxResults = 5;
}
