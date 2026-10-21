using Azure.AI.AgentServer.Core;
using Compass.Agents.Hosting;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Contracts.Tests.Hosting;

public sealed class AgentCallContextFactoryTests
{
    private static Dictionary<string, string> Headers(params (string Name, string Value)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Only_x_client_headers_become_tokens_and_their_names_are_lower_cased()
    {
        var headers = Headers(
            ("X-Client-Graph-Access-Token", "graph-token"),
            ("x-client-zava-api-token", "zava-token"),
            ("x-compass-session-id", "s1"),
            ("authorization", "Bearer should-never-be-a-token"));

        var context = AgentCallContextFactory.FromForwarded(headers, PlatformContext.Empty);

        Assert.Equal(["x-client-graph-access-token", "x-client-zava-api-token"], context.ForwardedTokens.Keys.Order());
        Assert.DoesNotContain("should-never-be-a-token", context.ForwardedTokens.Values);
    }

    [Fact]
    public void Blank_token_headers_are_dropped_so_a_missing_token_still_fails_closed()
    {
        var context = AgentCallContextFactory.FromForwarded(Headers((CompassHeaders.GraphAccessToken, "  ")), PlatformContext.Empty);

        Assert.Empty(context.ForwardedTokens);
    }

    [Fact]
    public void The_platform_injected_user_id_wins_over_any_forwarded_identity_header()
    {
        var headers = Headers((CompassHeaders.UserIdentity, "spoofed"));

        var context = AgentCallContextFactory.FromForwarded(headers, new PlatformContext("oid-from-platform", "call-1"));

        Assert.Equal("oid-from-platform", context.PersonObjectId);
    }

    [Fact]
    public void Falls_back_to_the_identity_header_when_the_platform_supplies_no_user()
    {
        var context = AgentCallContextFactory.FromForwarded(Headers((CompassHeaders.UserIdentity, "oid-local")), PlatformContext.Empty);

        Assert.Equal("oid-local", context.PersonObjectId);
    }
}
