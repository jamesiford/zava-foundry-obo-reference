using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;

namespace Compass.Agents.Contracts.Tests.Support;

/// <summary>Test double for <see cref="IGraphRetrievalClient"/>: records every call and returns or throws what the test sets up.</summary>
internal sealed class FakeGraphRetrievalClient : IGraphRetrievalClient
{
    public List<(string Token, string Query, int MaxResults)> Calls { get; } = [];

    public RetrievalResult Result { get; set; } = new([]);

    public Exception? Failure { get; set; }

    public Task<RetrievalResult> RetrieveAsync(string graphToken, string query, int maxResults, CancellationToken ct)
    {
        Calls.Add((graphToken, query, maxResults));
        return Failure is null ? Task.FromResult(Result) : Task.FromException<RetrievalResult>(Failure);
    }
}
