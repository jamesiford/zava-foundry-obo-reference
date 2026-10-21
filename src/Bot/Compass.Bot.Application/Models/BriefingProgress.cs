namespace Compass.Bot.Application.Models;

/// <summary>Where a turn is in its life cycle.</summary>
public enum BriefingStage
{
    Started,
    BranchStarted,
    BranchCompleted,
    Composing,
    Completed,
    Failed,
}

/// <summary>The three parallel branches of the briefing workflow.</summary>
public enum BriefingBranch
{
    SharePointRetrieval,
    SharePointKnowledge,
    ZavaApi,
}

/// <summary>How a branch ended. <see cref="Withheld"/> means it succeeded but the person's permissions left nothing to show.</summary>
public enum BranchOutcome
{
    Succeeded,
    Failed,
    Withheld,
}

/// <summary>
/// One advisory step of a turn, for people watching it run (Teams informative updates, the web page).
/// </summary>
/// <remarks>
/// Every member is an enum or a number on purpose. A progress event crosses into places that are not covered by the
/// identity contract (a chat, a browser, a log line), so it must be structurally unable to carry document text, member
/// data, tokens or exception messages. <c>BriefingProgressTests</c> enforces that no member can hold free text.
/// </remarks>
/// <param name="Stage">Life-cycle step.</param>
/// <param name="Branch">The branch concerned, for the two branch stages and for a failure caused by one.</param>
/// <param name="Outcome">How the branch ended, for <see cref="BriefingStage.BranchCompleted"/> and a branch failure.</param>
/// <param name="ElapsedMilliseconds">Time since the turn's workflow started.</param>
/// <param name="Route">The SharePoint route in use, so a viewer can see which branches to expect.</param>
public sealed record BriefingProgress(
   BriefingStage Stage,
   BriefingBranch? Branch,
   BranchOutcome? Outcome,
   long ElapsedMilliseconds,
   SharePointRoute Route);
