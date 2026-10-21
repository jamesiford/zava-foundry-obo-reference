using System.Text.Json;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Contracts.Tests.Support;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Contracts.Tests.Contracts;

public sealed class ContractShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Header_names_are_the_wire_names_the_platform_forwards()
    {
        Assert.Equal("x-ms-user-identity", CompassHeaders.UserIdentity);
        Assert.Equal("x-client-graph-access-token", CompassHeaders.GraphAccessToken);
        Assert.Equal("x-client-zava-api-token", CompassHeaders.ZavaApiToken);
        Assert.Equal("x-agent-user-id", CompassHeaders.AgentUserId);
    }

    [Fact]
    public void AgentCallContext_ToString_lists_header_names_but_never_token_values()
    {
        var token = FakeTokens.Create("secret-marker");
        var context = TestContexts.With("oid-1", (CompassHeaders.GraphAccessToken, token));

        var text = context.ToString();

        Assert.DoesNotContain(token, text);
        Assert.DoesNotContain("secret-marker", text);
        Assert.Contains(CompassHeaders.GraphAccessToken, text);
        Assert.Contains("oid-1", text);
    }

    [Fact]
    public void AgentCallContext_string_interpolation_and_formatting_never_expose_the_token()
    {
        var token = FakeTokens.Create("interp-marker");
        var context = TestContexts.With("oid-1", (CompassHeaders.ZavaApiToken, token));

        Assert.DoesNotContain(token, $"{context}");
        Assert.DoesNotContain(token, string.Format("{0}", context));
    }

    [Fact]
    public void RequireToken_returns_the_forwarded_token_unchanged()
    {
        var token = FakeTokens.Create("x");
        var context = TestContexts.With("oid", (CompassHeaders.GraphAccessToken, token));

        Assert.Equal(token, context.RequireToken(CompassHeaders.GraphAccessToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireToken_fails_closed_when_the_header_is_absent_or_blank(string? value)
    {
        var context = value is null
            ? TestContexts.Empty()
            : TestContexts.With("oid", (CompassHeaders.GraphAccessToken, value));

        var ex = Assert.Throws<MissingDownstreamTokenException>(() => context.RequireToken(CompassHeaders.GraphAccessToken));
        Assert.Equal(CompassHeaders.GraphAccessToken, ex.HeaderName);
    }

    [Fact]
    public void MissingDownstreamTokenException_names_the_header_only()
    {
        var ex = new MissingDownstreamTokenException(CompassHeaders.ZavaApiToken);

        Assert.Contains(CompassHeaders.ZavaApiToken, ex.Message);
    }

    [Fact]
    public void Retrieval_contract_round_trips_through_json()
    {
        var result = new RetrievalResult([new RetrievalHit("https://contoso.sharepoint.com/a", "Lending Policy", "text", 0.9)]);

        var back = JsonSerializer.Deserialize<RetrievalResult>(JsonSerializer.Serialize(result, Web), Web);

        Assert.NotNull(back);
        Assert.Equal("Lending Policy", back.Hits.Single().Title);
        Assert.Equal(0.9, back.Hits.Single().Relevance);
    }

    [Fact]
    public void Briefing_contract_round_trips_through_json()
    {
        var result = new MemberBriefingResult("ZCU-1", "A B", "Active", [new AccountBriefing("1-S", "Savings", "Rainy day", 12.5m, "USD")], 12.5m);

        var back = JsonSerializer.Deserialize<MemberBriefingResult>(JsonSerializer.Serialize(result, Web), Web);

        Assert.NotNull(back);
        Assert.Equal("ZCU-1", back.MemberNumber);
        Assert.Equal(12.5m, back.Accounts.Single().Balance);
    }
}
