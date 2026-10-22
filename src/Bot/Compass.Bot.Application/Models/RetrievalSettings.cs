namespace Compass.Bot.Application.Models;

/// <summary>
/// How the Graph retrieval route (route A) turns the store's hits into an answer. Bound from
/// <c>Bot:SharePoint:Retrieval</c>; the host validates it (names only) in <c>/health/ready</c>.
/// </summary>
/// <param name="MinRelevance">
/// Hits whose <c>relevanceScore</c> is below this are dropped before anything is shown or summarised. 0 disables the cut
/// (every score is at or above 0), which is the default until the value has been measured.
/// </param>
/// <param name="Summarise">
/// Whether the surviving extracts are sent to the answer prompt agent. When off, the extracts are shown as they come back.
/// </param>
/// <param name="SummaryTimeout">How long the answer agent gets before the turn falls back to the extracts.</param>
public sealed record RetrievalSettings(double MinRelevance = 0, bool Summarise = false, TimeSpan? SummaryTimeout = null)
{
    public const string SectionName = "Bot:SharePoint:Retrieval";

    public const string MinRelevanceKey = $"{SectionName}:MinRelevance";

    public const string SummariseKey = $"{SectionName}:Summarise";

    /// <summary>What the host uses when nothing is configured: no relevance cut, summarisation on.</summary>
    public static readonly RetrievalSettings HostDefault = new(0, true);

    /// <summary>The effective timeout for the answer agent call.</summary>
    public TimeSpan Timeout => SummaryTimeout ?? TimeSpan.FromSeconds(30);
}
