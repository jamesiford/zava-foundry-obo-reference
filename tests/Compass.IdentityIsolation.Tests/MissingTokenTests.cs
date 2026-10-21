using System.Net;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Responses;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.IdentityIsolation.Tests.Support;
using Compass.Shared.Identity.Tokens;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// Case 4 (dev spec §12): with no downstream token an agent fails closed — a 401 <c>missing_token</c> — and never
/// reaches its downstream, so there is no app-only fallback that could see more than the person may.
/// </summary>
public sealed class MissingTokenTests
{
    public static TheoryData<string?> TokenHeaderValues => new() { null, string.Empty, "   " };

    [Theory]
    [MemberData(nameof(TokenHeaderValues))]
    public async Task SharePointAgent_WithNoGraphToken_FailsClosedAndNeverCallsGraph(string? headerValue)
    {
        await using var backend = await FakeBackend.StartAsync();

        using var response = await Post(backend.SharePointAgentClient, new RetrievalRequest("policies"), CompassHeaders.GraphAccessToken, headerValue);

        await AssertMissingTokenAsync(response);
        Assert.Empty(backend.Graph.Calls);
    }

    [Theory]
    [MemberData(nameof(TokenHeaderValues))]
    public async Task ZavaApiAgent_WithNoApiToken_FailsClosedAndNeverCallsTheApi(string? headerValue)
    {
        await using var backend = await FakeBackend.StartAsync();

        using var response = await Post(backend.ZavaApiAgentClient, new MemberBriefingRequest(TestPerson.UserA.MemberNumber), CompassHeaders.ZavaApiToken, headerValue);

        await AssertMissingTokenAsync(response);
        Assert.Empty(backend.ZavaApi.Calls);
    }

    [Fact]
    public async Task SharePointAgent_GivenOnlyTheOtherAgentsToken_StillFailsClosed()
    {
        await using var backend = await FakeBackend.StartAsync();
        var zavaToken = backend.Tokens.Mint(TestPerson.UserA, "ZavaApi");

        // A token for a different downstream must not be mistaken for the Graph token.
        using var response = await Post(backend.SharePointAgentClient, new RetrievalRequest("policies"), CompassHeaders.ZavaApiToken, zavaToken);

        await AssertMissingTokenAsync(response);
        Assert.Empty(backend.Graph.Calls);
    }

    [Fact]
    public async Task Gateway_WithNoForwardedTokens_SurfacesTheTypedMissingTokenFailure()
    {
        await using var backend = await FakeBackend.StartAsync();
        var person = new PersonContext(TestPerson.UserA.ObjectId, TestPerson.UserA.TenantId, TestPerson.UserA.DisplayName);
        var call = new HostedAgentCall(HostedAgentName.SharePointRetrieval, person, SessionId.Mint(person), new Dictionary<string, AccessToken>());

        await Assert.ThrowsAsync<MissingDownstreamTokenException>(
            () => backend.Gateway.InvokeAsync<RetrievalRequest, RetrievalResult>(call, new RetrievalRequest("policies"), CancellationToken.None));

        Assert.Empty(backend.Graph.Calls);
    }

    private static async Task<HttpResponseMessage> Post<T>(HttpClient client, T body, string header, string? value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/responses") { Content = new StringContent(ResponsesWire.CreateRequestBody("agent", body), System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add(CompassHeaders.AgentUserId, TestPerson.UserA.ObjectId);
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertMissingTokenAsync(HttpResponseMessage response)
    {
        // The hosting adapter reports an agent failure as a failed response (HTTP 200), not a 401.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"failed\"", body, StringComparison.Ordinal);
        Assert.Equal(AgentErrorCodes.MissingToken, ResponsesWire.ReadError(body)?.Code);
    }
}
