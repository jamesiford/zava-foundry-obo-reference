using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;

namespace Compass.Bot.Application.Models;

/// <summary>
/// Builds what the answer agent is sent, and turns its reply back into a result. Pure, so the exact input (the only thing
/// the model ever sees about a person's turn) can be asserted in a test.
/// </summary>
/// <remarks>
/// The input is the question and this turn's extracts, numbered and fenced as data. Nothing else goes in: no person id,
/// token, session id, title, address or history. Extract text is untrusted (a document may contain instructions), so
/// angle brackets are escaped and a document cannot close its own fence.
/// </remarks>
public static partial class ExtractAnswerPrompt
{
    public static string Build(string question, IReadOnlyList<RetrievalHit> extracts)
    {
        ArgumentNullException.ThrowIfNull(extracts);

        var text = new StringBuilder();
        text.AppendLine("<question>");
        text.AppendLine(Escape(question));
        text.AppendLine("</question>");
        text.AppendLine();
        text.AppendLine("<extracts>");
        for (var i = 0; i < extracts.Count; i++)
        {
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);
            text.AppendLine($"<extract number=\"{number}\">");
            text.AppendLine(Escape(extracts[i].Text));
            text.AppendLine("</extract>");
        }

        text.Append("</extracts>");
        return text.ToString();
    }

    /// <summary>
    /// Maps the reply to a <see cref="KnowledgeQueryResult"/>: the extract numbers the model cited (<c>[1]</c>) become the
    /// sources, in the order first cited, and the markers are removed from the text. A number that is not one of this
    /// turn's extracts is ignored, so the model cannot cite a document it was not given.
    /// </summary>
    public static KnowledgeQueryResult ToResult(string? reply, IReadOnlyList<RetrievalHit> extracts)
    {
        ArgumentNullException.ThrowIfNull(extracts);

        var text = reply?.Trim() ?? string.Empty;
        var sources = new List<KnowledgeSource>();
        var seen = new HashSet<int>();
        foreach (Match marker in CitationMarker().Matches(text))
        {
            if (int.TryParse(marker.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number >= 1 && number <= extracts.Count && seen.Add(number))
            {
                var hit = extracts[number - 1];
                sources.Add(new KnowledgeSource(hit.Title ?? hit.SiteUrl, hit.SiteUrl));
            }
        }

        var answer = CitationMarkerWithSpace().Replace(text, string.Empty).Trim();
        return new KnowledgeQueryResult(answer, sources);
    }

    private static string Escape(string? value) => (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    [GeneratedRegex(@"\[(\d{1,3})\]")]
    private static partial Regex CitationMarker();

    [GeneratedRegex(@"[ \t]*\[\d{1,3}\]")]
    private static partial Regex CitationMarkerWithSpace();
}
