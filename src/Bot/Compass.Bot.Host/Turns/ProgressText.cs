using System.Text;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Host.Turns;

/// <summary>
/// Turns a <see cref="BriefingProgress"/> into the one line a person sees while waiting. The only input besides the
/// enums is the person's display name, taken from the Teams activity: no token, member data or retrieved text can get in.
/// </summary>
public static class ProgressText
{
    private const int MaxNameLength = 80;

    public static string? For(BriefingProgress progress, string? displayName) => (progress.Stage, progress.Branch) switch
    {
        (BriefingStage.BranchStarted, BriefingBranch.SharePointRetrieval or BriefingBranch.SharePointKnowledge) =>
            $"Reading SharePoint as {Clean(displayName)}…",
        (BriefingStage.BranchStarted, BriefingBranch.ZavaApi) => "Checking your member record…",
        (BriefingStage.Composing, _) => "Composing…",
        _ => null,
    };

    // Display names are user-controlled text going into a chat message: strip control characters and cap the length.
    private static string Clean(string? displayName)
    {
        var name = new StringBuilder();
        foreach (var c in displayName ?? string.Empty)
        {
            if (!char.IsControl(c))
            {
                name.Append(c);
            }
        }

        var cleaned = name.ToString().Trim();
        if (cleaned.Length > MaxNameLength)
        {
            cleaned = cleaned[..MaxNameLength];
        }

        return cleaned.Length == 0 ? "you" : cleaned;
    }
}
