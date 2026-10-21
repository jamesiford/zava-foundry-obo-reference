using System.Net;
using System.Text;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;

namespace Compass.Agents.Contracts.Tests.Clients;

public sealed class GraphRetrievalClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static GraphRetrievalClient ClientFor(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_the_person_token_as_bearer_and_maps_every_extract_to_a_hit()
    {
        var token = FakeTokens.Create("graph");
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"retrievalHits":[
              {"webUrl":"https://c.sharepoint.com/a.docx","extracts":[{"text":"one","relevanceScore":0.8},{"text":"two","relevanceScore":0.4}],"resourceMetadata":{"title":"Lending Policy"}},
              {"webUrl":"https://c.sharepoint.com/b.docx","extracts":[{"text":"three"}]}]}
            """));

        var result = await ClientFor(handler).RetrieveAsync(token, "lending", 5, CancellationToken.None);

        Assert.Equal("Bearer", handler.Last!.Headers.Authorization!.Scheme);
        Assert.Equal(token, handler.Last.Headers.Authorization.Parameter);
        Assert.Equal("https://graph.test/v1.0/copilot/retrieval", handler.Last.RequestUri!.ToString());
        Assert.Contains("\"queryString\":\"lending\"", handler.LastBody);
        Assert.Contains("\"dataSource\":\"sharePoint\"", handler.LastBody);
        Assert.Equal(3, result.Hits.Count);
        Assert.Equal("Lending Policy", result.Hits[0].Title);
        Assert.Null(result.Hits[2].Title);
    }

    [Fact]
    public async Task A_401_becomes_a_typed_authentication_failure_without_the_token()
    {
        var token = FakeTokens.Create("expired");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<DownstreamAuthenticationException>(
            () => ClientFor(handler).RetrieveAsync(token, "q", 5, CancellationToken.None));

        Assert.DoesNotContain(token, ex.ToString());
    }

    [Fact]
    public async Task A_claims_challenge_is_surfaced_with_its_claims()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnt9fQ\"");
            return response;
        });

        var ex = await Assert.ThrowsAsync<ClaimsChallengeException>(
            () => ClientFor(handler).RetrieveAsync(FakeTokens.Create("t"), "q", 5, CancellationToken.None));

        Assert.Equal("eyJhY2Nlc3NfdG9rZW4iOnt9fQ", ex.Claims);
    }
}
