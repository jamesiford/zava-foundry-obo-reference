using Compass.Bot.Application.Models;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration;

/// <summary>
/// Translates workflow events into <see cref="BriefingProgress"/>. It reads only which executor an event concerns and
/// whether a branch produced nothing — never the payload — so nothing a person may not see can reach a progress sink.
/// </summary>
/// <remarks>One instance per run: it holds the run's stopwatch and which branches have already been reported.</remarks>
internal sealed class BriefingProgressTracker(SharePointRoute route)
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly HashSet<BriefingBranch> _completed = [];
    private bool _composing;
    private bool _failed;

    public BriefingProgress Started() => New(BriefingStage.Started);

    /// <summary>Maps one workflow event to zero or more progress steps.</summary>
    public IEnumerable<BriefingProgress> Map(WorkflowEvent evt)
    {
        switch (evt)
        {
            case ExecutorInvokedEvent invoked when BranchOf(invoked.ExecutorId) is { } branch:
                yield return New(BriefingStage.BranchStarted, branch);
                break;

            case ExecutorInvokedEvent invoked when invoked.ExecutorId == BriefingExecutorIds.Compose && !_composing:
                _composing = true;
                yield return New(BriefingStage.Composing);
                break;

            case ExecutorCompletedEvent completed when BranchOf(completed.ExecutorId) is { } branch && _completed.Add(branch):
                var outcome = completed.Data is BranchResult { Documents.Hits.Count: 0 } ? BranchOutcome.Withheld : BranchOutcome.Succeeded;
                yield return New(BriefingStage.BranchCompleted, branch, outcome);
                break;

            case WorkflowOutputEvent { Data: MemberBriefing }:
                yield return New(BriefingStage.Completed);
                break;
        }
    }

    /// <summary>The terminal step for a failure, or <see langword="null"/> if one was already reported; names the branch when the failing executor was one.</summary>
    public BriefingProgress? Failed(WorkflowEvent? evt)
    {
        if (_failed)
        {
            return null;
        }

        _failed = true;
        var branch = evt is ExecutorEvent executor ? BranchOf(executor.ExecutorId) : null;
        return New(BriefingStage.Failed, branch, branch is null ? null : BranchOutcome.Failed);
    }

    private static BriefingBranch? BranchOf(string executorId) => executorId switch
    {
        BriefingExecutorIds.Retrieval => BriefingBranch.SharePointRetrieval,
        BriefingExecutorIds.Knowledge => BriefingBranch.SharePointKnowledge,
        BriefingExecutorIds.ZavaApi => BriefingBranch.ZavaApi,
        _ => null,
    };

    private BriefingProgress New(BriefingStage stage, BriefingBranch? branch = null, BranchOutcome? outcome = null) =>
        new(stage, branch, outcome, _clock.ElapsedMilliseconds, route);
}

/// <summary>The executor ids, shared so the graph and the progress mapping can't drift apart.</summary>
internal static class BriefingExecutorIds
{
    public const string Start = "briefing-start";
    public const string Retrieval = "sharepoint-retrieval";
    public const string Knowledge = "sharepoint-knowledge";
    public const string ZavaApi = "zava-api";
    public const string Compose = "briefing-compose";
}
