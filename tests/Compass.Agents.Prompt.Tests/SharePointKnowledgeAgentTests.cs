using System.Net;
using System.Text.Json;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.Tests.Support;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Prompt.Tests;

/// <summary>Contract rules 2 and 5 for the knowledge agent, asserted on the request the SDK actually sends.</summary>
public sealed class SharePointKnowledgeAgentTests
{
    private const string PersonId = "11111111-1111-1111-1111-111111111111";
    private const string SearchToken = "eyJhbGciOiJSUzI1NiJ9.search-token-of-maria.sig";

    private static SharePointKnowledgeAgent CreateAgent(RecordingHandler handler) =>
        new(FoundryWire.Factory(handler), FoundryWire.KnowledgeOptions());

    private static AgentCallContext WithToken(string? token = SearchToken) => new(
        PersonId,
        token is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [CompassHeaders.SearchToken] = token });

    [Fact]
    public async Task Forwards_the_search_token_unchanged_as_the_search_auth_token_structured_input()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("ok"));

        await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("What is the overdraft policy?"), WithToken(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(SearchToken, body.RootElement.GetProperty("structured_inputs").GetProperty("search_auth_token").GetString());
    }

    [Fact]
    public async Task Addresses_the_prompt_agent_by_reference_and_authenticates_as_the_bot_not_the_person()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("ok"));

        await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("SharePointKnowledgeAgent", body.RootElement.GetProperty("agent_reference").GetProperty("name").GetString());
        Assert.Equal("Bearer bot-managed-identity-token", request.Headers["Authorization"]);
        Assert.DoesNotContain(SearchToken, request.Headers.Values);
    }

    [Fact]
    public async Task Does_not_store_the_conversation_server_side()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("ok"));

        await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fails_closed_before_any_foundry_call_when_the_search_token_is_missing(string? token)
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("ok"));

        await Assert.ThrowsAsync<MissingDownstreamTokenException>(() =>
            CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(token), CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Maps_a_foundry_rejection_to_a_status_only_exception_that_never_contains_the_token()
    {
        var handler = new RecordingHandler(r => FoundryWire.Json($"{{\"error\":{{\"message\":\"bad token {SearchToken}\"}}}}", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<KnowledgeAgentException>(() =>
            CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        Assert.DoesNotContain(SearchToken, ex.ToString());
    }

    [Fact]
    public async Task Maps_a_failed_response_to_a_bad_gateway()
    {
        var handler = new RecordingHandler(_ => FoundryWire.Json("""
            { "id": "resp_1", "object": "response", "created_at": 1, "status": "failed", "model": "gpt",
              "error": { "code": "server_error", "message": "boom" }, "output": [] }
            """));

        var ex = await Assert.ThrowsAsync<KnowledgeAgentException>(() =>
            CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
    }

    [Fact]
    public async Task Returns_the_answer_with_the_distinct_sources_it_cited()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse(
            "Overdraft is $30 【4:0†Member Handbook.docx】 and waived for Gold 【4:1†Fee Schedule.pdf】 【4:2†Member Handbook.docx】."));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        Assert.Contains("Overdraft is $30", result.Answer);
        Assert.Equal(["Member Handbook.docx", "Fee Schedule.pdf"], result.Sources);
    }

    [Fact]
    public async Task An_answer_without_citations_has_no_sources()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("I don't know"));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        Assert.Equal("I don't know", result.Answer);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task Sources_come_from_the_citation_annotations_with_a_readable_title_and_the_real_link()
    {
        const string faq = "https://contoso.sharepoint.com/sites/demo/MemberServicesFAQ/Member%20Services%20FAQ.docx";
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponseWithCitations(
            "The cutoff is 3:30 PM Eastern. 【5:1†source】",
            (faq, "https://contoso.sharepoint.com/sites/demo/MemberServicesFAQ/Member Services FAQ.docx"),
            (faq, "https://contoso.sharepoint.com/sites/demo/MemberServicesFAQ/Member Services FAQ.docx")));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        var source = Assert.Single(result.Sources);
        Assert.Equal("Member Services FAQ", source.Title);
        Assert.Equal(faq, source.Url);
    }

    [Fact]
    public async Task The_models_inline_citation_markers_are_removed_from_the_answer()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponseWithCitations(
            "The ratio is **43%**. 【6:0†source】 Exceptions need VP approval 【6:1†source】.",
            ("https://contoso.sharepoint.com/sites/demo/LendingPolicy/Consumer%20Lending%20Policy.docx", "x")));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        Assert.DoesNotContain("【", result.Answer, StringComparison.Ordinal);
        Assert.DoesNotContain("†", result.Answer, StringComparison.Ordinal);
        Assert.Equal("The ratio is **43%**. Exceptions need VP approval.", result.Answer);
    }

    [Fact]
    public async Task A_marker_that_only_says_source_is_not_a_source_when_no_annotation_names_one()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponse("It depends. 【6:0†source】"));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        Assert.Empty(result.Sources);
        Assert.Equal("It depends.", result.Answer);
    }

    [Fact]
    public async Task Only_web_links_are_accepted_as_a_source_address()
    {
        var handler = new RecordingHandler(_ => FoundryWire.CompletedResponseWithCitations(
            "x", ("file:///c:/secrets.docx", "t"), ("javascript:alert(1)", "t")));

        var result = await CreateAgent(handler).HandleAsync(new KnowledgeQueryRequest("q"), WithToken(), CancellationToken.None);

        Assert.Empty(result.Sources);
    }
}