using Compass.Agents.Contracts.Retrieval;

namespace Compass.Agents.Hosted.SharePointRetrieval.Clients;

/// <summary>
/// Narrow seam over the Graph Copilot Retrieval API (dev spec §6, Interface Segregation): it exposes only what the
/// retrieval agent needs, and takes the person's token per call so no implementation can hold one longer.
/// </summary>
public interface IGraphRetrievalClient
{
    Task<RetrievalResult> RetrieveAsync(string graphToken, string query, int maxResults, CancellationToken ct);
}
