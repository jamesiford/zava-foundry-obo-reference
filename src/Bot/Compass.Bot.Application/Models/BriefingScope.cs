using Compass.Bot.Domain;

namespace Compass.Bot.Application.Models;

/// <summary>
/// Which branches one turn runs, decided before any token is exchanged: the person's accounts, Graph retrieval, and the
/// knowledge base. A branch that is not in scope is never built and its downstream token is never requested.
/// </summary>
public sealed record BriefingScope(bool Accounts, bool Retrieval, bool Knowledge)
{
    /// <summary>The downstream audiences this scope needs a token for, and no others.</summary>
    public IReadOnlyList<DownstreamAudience> Audiences =>
    [
        .. Retrieval ? [DownstreamAudience.Graph] : Array.Empty<DownstreamAudience>(),
        .. Knowledge ? [DownstreamAudience.Search] : Array.Empty<DownstreamAudience>(),
        .. Accounts ? [DownstreamAudience.ZavaApi] : Array.Empty<DownstreamAudience>(),
    ];

    public bool UsesSharePoint => Retrieval || Knowledge;
}
