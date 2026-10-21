using System.Collections.Concurrent;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Contracts.Knowledge;
using Compass.Shared.Identity.Tokens;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;
using Compass.Agents.Hosted.ZavaApi.Clients;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>One call that reached a fake downstream, with the person its token actually belonged to (null if rejected).</summary>
internal sealed record DownstreamCall(string Token, string? TokenOwnerObjectId);

/// <summary>
/// Fake SharePoint: the store decides what each token's owner can see, and the agent under test cannot influence it.
/// That is the point — if the real agent ever post-filtered or widened results, this fake would not mask it.
/// </summary>
internal sealed class DirectoryBackedGraphClient(TokenDirectory directory) : IGraphRetrievalClient
{
    private readonly ConcurrentQueue<DownstreamCall> _calls = new();

    public IReadOnlyCollection<DownstreamCall> Calls => _calls;

    public Task<RetrievalResult> RetrieveAsync(string graphToken, string query, int maxResults, CancellationToken ct)
    {
        _calls.Enqueue(new DownstreamCall(graphToken, directory.TryGet(graphToken, out var seen) ? seen.Person.ObjectId : null));
        var grant = directory.Authorize(graphToken, "Graph");
        return Task.FromResult(new RetrievalResult(DocumentsFor(grant.Person)));
    }

    private static List<RetrievalHit> DocumentsFor(TestPerson person)
    {
        var hits = new List<RetrievalHit>
        {
            new("https://zava.sharepoint.com/sites/Handbook", "Member Handbook", "Public member handbook, readable by everyone.", 0.4),
        };

        hits.Add(person.Key == "A"
            ? new RetrievalHit("https://zava.sharepoint.com/sites/Lending", "Lending Policy", $"Lending policy, restricted to lending staff. {TestPerson.CanaryA}", 0.9)
            : new RetrievalHit("https://zava.sharepoint.com/sites/Collections", "Collections Playbook", $"Collections playbook, restricted to collections staff. {TestPerson.CanaryB}", 0.9));
        return hits;
    }
}

/// <summary>
/// Fake Foundry IQ knowledge base: stands in for the whole prompt-agent → knowledge-base → SharePoint path. Like the real
/// agent it refuses to run without the person's Search token, and like the real knowledge base it decides what to return
/// from that token's owner alone.
/// </summary>
internal sealed class DirectoryBackedKnowledgeAgent(TokenDirectory directory) : IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>
{
    private readonly ConcurrentQueue<DownstreamCall> _calls = new();

    public IReadOnlyCollection<DownstreamCall> Calls => _calls;

    public Task<KnowledgeQueryResult> HandleAsync(KnowledgeQueryRequest request, AgentCallContext context, CancellationToken ct)
    {
        var token = context.RequireToken(CompassHeaders.SearchToken);
        _calls.Enqueue(new DownstreamCall(token, directory.TryGet(token, out var seen) ? seen.Person.ObjectId : null));
        var grant = directory.Authorize(token, "Search");

        var canary = grant.Person.Key == "A" ? TestPerson.CanaryA : TestPerson.CanaryB;
        return Task.FromResult(new KnowledgeQueryResult($"Knowledge base answer for {grant.Person.DisplayName}. {canary}", ["Member Handbook.docx"]));
    }
}

/// <summary>Fake Zava API: each token's owner may read only their own member record.</summary>
internal sealed class DirectoryBackedZavaApiClient(TokenDirectory directory) : IZavaApiMcpClient
{
    private readonly ConcurrentQueue<DownstreamCall> _calls = new();

    public IReadOnlyCollection<DownstreamCall> Calls => _calls;

    public Task<MemberBriefingResult> GetMemberBriefingAsync(string apiToken, string memberNumber, CancellationToken ct)
    {
        _calls.Enqueue(new DownstreamCall(apiToken, directory.TryGet(apiToken, out var seen) ? seen.Person.ObjectId : null));
        var grant = directory.Authorize(apiToken, "ZavaApi");

        // Asking for someone else's member number is refused by the store, not by the agent asking.
        if (!string.Equals(grant.Person.MemberNumber, memberNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownstreamAuthenticationException("ZavaApi", 403);
        }

        var balance = grant.Person.Key == "A" ? 1250.50m : 8420.10m;
        return Task.FromResult(new MemberBriefingResult(
            grant.Person.MemberNumber,
            grant.Person.DisplayName,
            "Active",
            [new AccountBriefing(grant.Person.Key == "A" ? "1001" : "2001", "Checking", "Everyday", balance, "USD")],
            balance));
    }
}
