using System.Globalization;
using System.Text;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;

namespace Compass.Bot.Application.Models;

/// <summary>
/// Turns the three agents' structured results into the single text answer shown in Teams. Pure and separate from the
/// orchestration so wording can change without touching the identity flow (dev spec §6, SRP).
/// </summary>
/// <remarks>
/// It shows exactly what ran. The scope for the turn is decided before the fan-out (see <see cref="BriefingScope"/>), so
/// a branch that did not run arrives as <see langword="null"/> and contributes nothing: an accounts question has no
/// document section, a policy question has no account block. Among the branches that did run, a section with nothing in
/// it is dropped, so a person never reads "I don't know" next to an answer they didn't ask for.
/// </remarks>
public static class BriefingComposer
{
    public static string Compose(
        MemberBriefingResult? accounts,
        RetrievalResult? documents,
        KnowledgeQueryResult? knowledge,
        SharePointRoute route)
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        if (accounts is not null)
        {
            text.AppendLine(culture, $"**{accounts.FullName}** ({accounts.MemberNumber}, {accounts.Status})");
            foreach (var account in accounts.Accounts)
            {
                text.AppendLine(culture, $"- {account.Nickname} {account.AccountNumber}: {account.Balance:N2} {account.Currency}");
            }

            text.AppendLine(culture, $"Net balance: {accounts.NetBalance:N2}");
        }

        var sharePointRan = documents is not null || knowledge is not null;
        if (sharePointRan)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }

            var sections = new StringBuilder();
            if (documents is { Hits.Count: > 0 })
            {
                sections.AppendLine("From SharePoint:");
                foreach (var hit in documents.Hits)
                {
                    sections.AppendLine(culture, $"- {hit.Title ?? hit.SiteUrl}: {hit.Text} ({hit.SiteUrl})");
                }
            }

            if (knowledge is not null && HasAnswer(knowledge))
            {
                if (sections.Length > 0)
                {
                    sections.AppendLine();
                }

                sections.AppendLine("From the knowledge base:");
                sections.AppendLine(knowledge.Answer);
                if (knowledge.Sources.Count > 0)
                {
                    sections.AppendLine(culture, $"Sources: {string.Join(", ", knowledge.Sources.Select(s => s.Title))}");
                }
            }

            // Empty means SharePoint showed this person nothing relevant, which is not the same as nothing existing.
            text.Append(sections.Length > 0 ? sections.ToString() : "No document you have access to answers this question.\n");
            text.AppendLine();
            text.AppendLine(culture, $"SharePoint route: {new BriefingRoutePolicy(route).Description}");
        }

        return text.ToString().TrimEnd();
    }
    /// <summary>The knowledge agent is told to answer exactly "I don't know" when nothing relevant is visible to the person.</summary>
    private static bool HasAnswer(KnowledgeQueryResult knowledge)
    {
        var answer = knowledge.Answer?.Trim().TrimEnd('.', '!') ?? string.Empty;
        return answer.Length > 0
            && !answer.Equals("I don't know", StringComparison.OrdinalIgnoreCase)
            && !answer.Equals("I don’t know", StringComparison.OrdinalIgnoreCase)
            && !answer.Equals("I do not know", StringComparison.OrdinalIgnoreCase);
    }
}
