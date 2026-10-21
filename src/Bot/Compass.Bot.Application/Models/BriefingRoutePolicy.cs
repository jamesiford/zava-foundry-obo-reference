using Compass.Bot.Domain;

namespace Compass.Bot.Application.Models;

/// <summary>Which SharePoint route(s) a deployment uses to ground an answer.</summary>
public enum SharePointRoute
{
    /// <summary>Foundry IQ knowledge base only (the default): the Search-scope token, no Graph token.</summary>
    KnowledgeBase,

    /// <summary>Graph retrieval only: the Graph-scope token, no Search token.</summary>
    Retrieval,

    /// <summary>Both routes side by side, for comparing them.</summary>
    Both,
}

/// <summary>
/// The composition-time answer to "which SharePoint routes does this deployment run?". It decides which workflow
/// branches exist and therefore which downstream tokens are ever acquired.
/// </summary>
/// <remarks>
/// A route is chosen once at startup rather than branched on inside an executor: a branch that was never built cannot
/// run, and a token that was never requested cannot be misused (least privilege per turn).
/// </remarks>
public sealed class BriefingRoutePolicy(SharePointRoute route)
{
    public SharePointRoute Route => route;

    public bool UsesRetrieval => route is SharePointRoute.Retrieval or SharePointRoute.Both;

    public bool UsesKnowledge => route is SharePointRoute.KnowledgeBase or SharePointRoute.Both;

    /// <summary>The downstream audiences a turn acquires tokens for: the Zava API always, SharePoint per route.</summary>
    public IReadOnlyList<DownstreamAudience> Audiences { get; } = BuildAudiences(route);

    /// <summary>
    /// The branches one turn runs: the deployment's route narrowed by what the question is about. It can only remove
    /// branches, never add one the route does not allow, so a per-turn decision can never widen what is reached.
    /// </summary>
    public BriefingScope ScopeFor(QuestionFocus focus) => new(
        Accounts: focus != QuestionFocus.Policy,
        Retrieval: UsesRetrieval && focus != QuestionFocus.Accounts,
        Knowledge: UsesKnowledge && focus != QuestionFocus.Accounts);

    /// <summary>A short phrase for naming the route in answers and logs.</summary>
    public string Description => route switch
    {
        SharePointRoute.KnowledgeBase => "knowledge base",
        SharePointRoute.Retrieval => "Graph retrieval",
        _ => "Graph retrieval and knowledge base",
    };

    /// <summary>
    /// Parses configuration; null or blank means the default. Only an exact route name (any case) is accepted: numbers
    /// and comma lists would otherwise coerce to a different route.
    /// </summary>
    public static bool TryParse(string? value, out SharePointRoute parsed)
    {
        parsed = SharePointRoute.KnowledgeBase;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var name = value.Trim();
        foreach (var candidate in Enum.GetValues<SharePointRoute>())
        {
            if (string.Equals(name, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                parsed = candidate;
                return true;
            }
        }

        return false;
    }

    private static DownstreamAudience[] BuildAudiences(SharePointRoute route) => route switch
    {
        SharePointRoute.KnowledgeBase => [DownstreamAudience.Search, DownstreamAudience.ZavaApi],
        SharePointRoute.Retrieval => [DownstreamAudience.Graph, DownstreamAudience.ZavaApi],
        _ => [DownstreamAudience.Graph, DownstreamAudience.Search, DownstreamAudience.ZavaApi],
    };
}
