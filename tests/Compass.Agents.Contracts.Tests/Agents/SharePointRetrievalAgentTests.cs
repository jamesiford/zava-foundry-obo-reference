using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Agents.Hosted.SharePointRetrieval;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Contracts.Tests.Agents;

public sealed class SharePointRetrievalAgentTests
{
    private readonly FakeGraphRetrievalClient _graph = new();

    private SharePointRetrievalAgent Agent => new(_graph);

    [Fact]
    public async Task Fails_closed_without_the_graph_token_and_makes_no_downstream_call()
    {
        // A Zava API token must not satisfy the Graph requirement.
        var context = TestContexts.With("oid", (CompassHeaders.ZavaApiToken, FakeTokens.Create("api")));

        var ex = await Assert.ThrowsAsync<MissingDownstreamTokenException>(
            () => Agent.HandleAsync(new RetrievalRequest("lending"), context, CancellationToken.None));

        Assert.Equal(CompassHeaders.GraphAccessToken, ex.HeaderName);
        Assert.Empty(_graph.Calls);
    }

    [Fact]
    public async Task Passes_the_forwarded_token_to_the_client_unchanged()
    {
        var token = FakeTokens.Create("graph-user-a");
        var context = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, token));

        await Agent.HandleAsync(new RetrievalRequest("lending", 3), context, CancellationToken.None);

        var call = Assert.Single(_graph.Calls);
        Assert.Equal(token, call.Token);
        Assert.Equal("lending", call.Query);
        Assert.Equal(3, call.MaxResults);
    }

    [Fact]
    public async Task Returns_exactly_what_the_store_returned_without_post_filtering()
    {
        var hits = new[]
        {
            new RetrievalHit("https://c.sharepoint.com/sites/lending/policy.docx", "Lending Policy", "a", 0.9),
            new RetrievalHit("https://c.sharepoint.com/sites/other/notes.docx", "Notes", "b", 0.01),
        };
        _graph.Result = new RetrievalResult(hits);
        var context = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, FakeTokens.Create("t")));

        var result = await Agent.HandleAsync(new RetrievalRequest("q"), context, CancellationToken.None);

        Assert.Equal(hits, result.Hits);
    }

    [Fact]
    public async Task Does_not_swallow_a_downstream_failure_into_a_success_result()
    {
        _graph.Failure = new DownstreamAuthenticationException("Graph", 401);
        var context = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, FakeTokens.Create("t")));

        await Assert.ThrowsAsync<DownstreamAuthenticationException>(
            () => Agent.HandleAsync(new RetrievalRequest("q"), context, CancellationToken.None));
    }

    [Fact]
    public async Task Exceptions_it_raises_never_contain_the_token()
    {
        var token = FakeTokens.Create("leak-check");
        _graph.Failure = new DownstreamAuthenticationException("Graph", 401);
        var withToken = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, token));

        var rejected = await Assert.ThrowsAsync<DownstreamAuthenticationException>(
            () => Agent.HandleAsync(new RetrievalRequest("q"), withToken, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<MissingDownstreamTokenException>(
            () => Agent.HandleAsync(new RetrievalRequest("q"), TestContexts.Empty(), CancellationToken.None));

        Assert.DoesNotContain(token, rejected.ToString());
        Assert.DoesNotContain(token, missing.ToString());
    }
}
