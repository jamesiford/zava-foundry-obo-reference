using System.ClientModel;
using System.Text.RegularExpressions;
using Azure.AI.Extensions.OpenAI;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Compass.Agents.Prompt.SharePointKnowledge;

/// <summary>
/// Calls the SharePoint knowledge prompt agent with the person's Search token as a structured input, so the Foundry IQ
/// knowledge base's remote SharePoint source answers as that person (second OBO route to SharePoint; the first is the
/// Graph retrieval agent).
/// </summary>
/// <remarks>
/// Contract rule 5 — fails closed: with no forwarded Search token it throws before any Foundry call, so the knowledge
/// base can never be queried unfiltered. Contract rule 2 — the token is forwarded exactly as received, used for this one
/// call, and never logged or placed in an exception.
/// </remarks>
public sealed partial class SharePointKnowledgeAgent(IFoundryResponsesClientFactory clients, IOptions<KnowledgeAgentOptions> options)
    : IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>
{
    private readonly KnowledgeAgentOptions _options = options.Value;

    public async Task<KnowledgeQueryResult> HandleAsync(KnowledgeQueryRequest request, AgentCallContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var searchToken = context.RequireToken(CompassHeaders.SearchToken);

        var client = clients.Create(new AgentReference(_options.AgentName, _options.AgentVersion));

        var create = new CreateResponseOptions { StoredOutputEnabled = false };
        create.InputItems.Add(ResponseItem.CreateUserMessageItem(request.Question));
        create.Patch.Set("$.structured_inputs.search_auth_token"u8, searchToken);

        ResponseResult response;
        try
        {
            response = (await client.CreateResponseAsync(create, ct)).Value;
        }
        catch (ClientResultException ex)
        {
            // ex.Message can echo the request; surface only the status.
            throw new KnowledgeAgentException(ex.Status, code: null);
        }

        if (response.Status == ResponseStatus.Failed || response.Error is not null)
        {
            throw new KnowledgeAgentException(statusCode: 502, response.Error?.Code.ToString());
        }

        var answer = response.GetOutputText();
        var sources = ExtractSources(response, answer);
        return new KnowledgeQueryResult(StripMarkers(answer), sources);
    }

    // The URLs come from the Responses annotations, which carry the real SharePoint address; the model's own marker only
    // ever names the source as the literal word "source", so it is used as a fallback and never as a title.
    private static IReadOnlyList<KnowledgeSource> ExtractSources(ResponseResult response, string answer)
    {
        var sources = new List<KnowledgeSource>();
        foreach (var item in response.OutputItems.OfType<MessageResponseItem>())
        {
            foreach (var part in item.Content)
            {
                foreach (var citation in (part.OutputTextAnnotations ?? []).OfType<UriCitationMessageAnnotation>())
                {
                    if (citation.Uri is { IsAbsoluteUri: true } uri && uri.Scheme is "https" or "http")
                    {
                        sources.Add(new KnowledgeSource(TitleFor(uri, citation.Title), uri.AbsoluteUri));
                    }
                }
            }
        }

        if (sources.Count == 0)
        {
            sources.AddRange(CitationPattern().Matches(answer)
                .Select(m => m.Groups["source"].Value.Trim())
                .Where(name => name.Length > 0 && !name.Equals("source", StringComparison.OrdinalIgnoreCase))
                .Select(name => new KnowledgeSource(name)));
        }

        return sources.DistinctBy(s => s.Url ?? s.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // SharePoint reports the file URL as the title; the file name without its extension is what a person recognises.
    private static string TitleFor(Uri uri, string? reportedTitle)
    {
        if (!string.IsNullOrWhiteSpace(reportedTitle) && !Uri.TryCreate(reportedTitle, UriKind.Absolute, out _))
        {
            return reportedTitle.Trim();
        }

        var name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath));
        return string.IsNullOrWhiteSpace(name) ? uri.Host : name;
    }

    private static string StripMarkers(string answer) =>
        Whitespace().Replace(MarkerWithLeadingSpace().Replace(answer, string.Empty), " ").Trim();

    // The knowledge base MCP tool's citation marker: 【message_idx:search_idx†source_name】
    [GeneratedRegex("【[^】†]*†(?<source>[^】]+)】")]
    private static partial Regex CitationPattern();

    [GeneratedRegex(@"\s*【[^】]*】")]
    private static partial Regex MarkerWithLeadingSpace();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Whitespace();
}
