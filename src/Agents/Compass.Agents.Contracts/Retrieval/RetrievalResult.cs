namespace Compass.Agents.Contracts.Retrieval;

/// <summary>
/// What SharePoint returned for the person. Deliberately unfiltered by the agent: SharePoint already applied the
/// person's permissions, so an empty list means "nothing you may see", not "nothing exists" (contract rule 4).
/// </summary>
public sealed record RetrievalResult(IReadOnlyList<RetrievalHit> Hits);

/// <summary>One retrieved passage and where it came from.</summary>
public sealed record RetrievalHit(string SiteUrl, string? Title, string Text, double? Relevance);
