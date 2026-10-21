using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Retrieval;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Hosted.SharePointRetrieval.Clients;

/// <summary>
/// Calls <c>POST /v1.0/copilot/retrieval</c> with the person's delegated token (<c>Files.Read.All</c>,
/// <c>Sites.Read.All</c>). Translates auth failures into typed exceptions so the bot can re-acquire or re-challenge.
/// </summary>
public sealed class GraphRetrievalClient(HttpClient http) : IGraphRetrievalClient
{
    public const string DownstreamName = "Microsoft Graph retrieval";

    public const string RetrievalPath = "v1.0/copilot/retrieval";

    public async Task<RetrievalResult> RetrieveAsync(string graphToken, string query, int maxResults, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, RetrievalPath)
        {
            Content = JsonContent.Create(new RetrievalPayload(query, "sharePoint", maxResults)),
        };

        // Graph, unlike the x-client-* hop, requires the standard Bearer scheme.
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);

        using var response = await http.SendAsync(message, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ThrowTyped(response);
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RetrievalResponse>(ct) ?? new RetrievalResponse(null);

        // One hit per extract: the extract is the passage the model can quote, the webUrl is where it lives.
        var hits = (body.RetrievalHits ?? [])
            .SelectMany(h => (h.Extracts ?? []).Select(e => new RetrievalHit(h.WebUrl ?? string.Empty, h.ResourceMetadata?.Title, e.Text ?? string.Empty, e.RelevanceScore)))
            .ToList();

        return new RetrievalResult(hits);
    }

    private static void ThrowTyped(HttpResponseMessage response)
    {
        var challenge = response.Headers.WwwAuthenticate.Select(h => h.ToString());
        if (ClaimsChallengeParser.TryGetClaims(challenge, out var claims))
        {
            throw new ClaimsChallengeException(DownstreamName, claims);
        }

        throw new DownstreamAuthenticationException(DownstreamName, (int)response.StatusCode);
    }

    private sealed record RetrievalPayload(
        [property: JsonPropertyName("queryString")] string QueryString,
        [property: JsonPropertyName("dataSource")] string DataSource,
        [property: JsonPropertyName("maximumNumberOfResults")] int MaximumNumberOfResults);

    private sealed record RetrievalResponse([property: JsonPropertyName("retrievalHits")] List<Hit>? RetrievalHits);

    private sealed record Hit(
        [property: JsonPropertyName("webUrl")] string? WebUrl,
        [property: JsonPropertyName("extracts")] List<Extract>? Extracts,
        [property: JsonPropertyName("resourceMetadata")] Metadata? ResourceMetadata);

    private sealed record Extract(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("relevanceScore")] double? RelevanceScore);

    private sealed record Metadata([property: JsonPropertyName("title")] string? Title);
}
