using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Agents.Hosted.ZavaApi;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Contracts.Tests.Agents;

public sealed class ZavaApiAgentTests
{
    private readonly FakeZavaApiMcpClient _mcp = new();

    private ZavaApiAgent Agent => new(_mcp);

    [Fact]
    public async Task Fails_closed_without_the_api_token_and_makes_no_downstream_call()
    {
        var context = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, FakeTokens.Create("graph")));

        var ex = await Assert.ThrowsAsync<MissingDownstreamTokenException>(
            () => Agent.HandleAsync(new MemberBriefingRequest("ZCU-100234"), context, CancellationToken.None));

        Assert.Equal(CompassHeaders.ZavaApiToken, ex.HeaderName);
        Assert.Empty(_mcp.Calls);
    }

    [Fact]
    public async Task Forwards_the_token_unchanged_with_no_re_minting()
    {
        var token = FakeTokens.Create("api-user-b");
        var context = TestContexts.With("oid", (CompassHeaders.ZavaApiToken, token));

        await Agent.HandleAsync(new MemberBriefingRequest("ZCU-100234"), context, CancellationToken.None);

        var call = Assert.Single(_mcp.Calls);
        Assert.Equal(token, call.Token);
        Assert.Equal("ZCU-100234", call.MemberNumber);
    }

    [Fact]
    public async Task Returns_the_clients_result_as_is()
    {
        var context = TestContexts.With("oid", (CompassHeaders.ZavaApiToken, FakeTokens.Create("t")));

        var result = await Agent.HandleAsync(new MemberBriefingRequest("ZCU-000001"), context, CancellationToken.None);

        Assert.Same(_mcp.Result, result);
    }

    [Fact]
    public async Task Exceptions_it_raises_never_contain_the_token()
    {
        var token = FakeTokens.Create("leak-check-api");
        _mcp.Failure = new DownstreamAuthenticationException("Zava API", 401);
        var withToken = TestContexts.With("oid", (CompassHeaders.ZavaApiToken, token));

        var rejected = await Assert.ThrowsAsync<DownstreamAuthenticationException>(
            () => Agent.HandleAsync(new MemberBriefingRequest("m"), withToken, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<MissingDownstreamTokenException>(
            () => Agent.HandleAsync(new MemberBriefingRequest("m"), TestContexts.Empty(), CancellationToken.None));

        Assert.DoesNotContain(token, rejected.ToString());
        Assert.DoesNotContain(token, missing.ToString());
    }
}
