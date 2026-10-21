using Compass.Bot.Application.Exceptions;
using Compass.Bot.Domain;
using Compass.Bot.Infrastructure.Obo;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace Compass.Bot.Application.Tests;

public sealed class OboTokenBrokerTests
{
    private sealed class FakeClient : IOnBehalfOfClient
    {
        public Exception? Throw { get; init; }

        public List<(string[] Scopes, string Assertion)> Calls { get; } = [];

        public Task<string> AcquireAccessTokenAsync(IReadOnlyCollection<string> scopes, string userAssertion, CancellationToken cancellationToken)
        {
            Calls.Add((scopes.ToArray(), userAssertion));
            return Throw is null ? Task.FromResult("downstream-token") : Task.FromException<string>(Throw);
        }
    }

    private static OboTokenBroker Create(FakeClient client, Action<OnBehalfOfOptions>? configure = null)
    {
        var options = new OnBehalfOfOptions { ZavaApiScopes = ["api://zava/.default"] };
        configure?.Invoke(options);
        return new OboTokenBroker(client, Options.Create(options));
    }

    [Fact]
    public async Task Graph_exchange_uses_the_graph_scopes_and_the_callers_assertion()
    {
        var client = new FakeClient();

        var token = await Create(client).AcquireAsync(new AccessToken("sso"), DownstreamAudience.Graph, CancellationToken.None);

        Assert.Equal("downstream-token", token.Reveal());
        var call = Assert.Single(client.Calls);
        Assert.Equal(["https://graph.microsoft.com/.default"], call.Scopes);
        Assert.Equal("sso", call.Assertion);
    }

    [Fact]
    public async Task Zava_api_exchange_uses_the_zava_api_scopes()
    {
        var client = new FakeClient();

        await Create(client).AcquireAsync(new AccessToken("sso"), DownstreamAudience.ZavaApi, CancellationToken.None);

        Assert.Equal(["api://zava/.default"], Assert.Single(client.Calls).Scopes);
    }

    [Fact]
    public async Task Search_exchange_uses_the_azure_ai_search_scope()
    {
        var client = new FakeClient();

        await Create(client).AcquireAsync(new AccessToken("sso"), DownstreamAudience.Search, CancellationToken.None);

        Assert.Equal(["https://search.azure.com/.default"], Assert.Single(client.Calls).Scopes);
    }

    [Fact]
    public async Task Missing_scopes_fail_before_any_exchange_is_attempted()
    {
        var client = new FakeClient();

        var ex = await Assert.ThrowsAsync<TokenAcquisitionException>(() =>
            Create(client, o => o.ZavaApiScopes = []).AcquireAsync(new AccessToken("sso"), DownstreamAudience.ZavaApi, CancellationToken.None));

        Assert.Equal(DownstreamAudience.ZavaApi, ex.Audience);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Interaction_required_becomes_a_typed_failure_that_keeps_the_inner_exception()
    {
        var inner = new MsalUiRequiredException("interaction_required", "consent needed");

        var ex = await Assert.ThrowsAsync<TokenAcquisitionException>(() =>
            Create(new FakeClient { Throw = inner }).AcquireAsync(new AccessToken("sso"), DownstreamAudience.Graph, CancellationToken.None));

        Assert.Equal(DownstreamAudience.Graph, ex.Audience);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task Other_msal_failures_become_typed_failures_and_never_echo_the_assertion()
    {
        var ex = await Assert.ThrowsAsync<TokenAcquisitionException>(() =>
            Create(new FakeClient { Throw = new MsalServiceException("invalid_grant", "bad grant") })
                .AcquireAsync(new AccessToken("sso-secret"), DownstreamAudience.Graph, CancellationToken.None));

        Assert.Contains("invalid_grant", ex.Message);
        Assert.DoesNotContain("sso-secret", ex.ToString());
    }

    [Fact]
    public async Task Non_msal_failures_are_not_swallowed()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Create(new FakeClient { Throw = new TimeoutException() }).AcquireAsync(new AccessToken("sso"), DownstreamAudience.Graph, CancellationToken.None));
    }
}
