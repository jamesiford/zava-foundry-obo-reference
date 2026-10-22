using System.Runtime.ExceptionServices;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Shared.Messaging;
using Microsoft.Agents.AI.Workflows;

namespace Compass.Bot.Orchestration;

/// <summary>
/// Runs the briefing as a Microsoft Agent Framework workflow and translates its event stream back into the plain
/// result-or-typed-exception contract the bot expects.
/// </summary>
/// <remarks>
/// The workflow engine reports node failures as events rather than throwing. Left like that, a missing token or a
/// claims challenge would surface as a generic workflow error and the bot could not tell the person to sign in again.
/// So the original exception is captured and rethrown, and the run is abandoned on the first failure: no later branch
/// output can complete a briefing after another branch has failed (contract rule 5).
/// </remarks>
public sealed class WorkflowBriefingOrchestrator(
    ISender sender,
    BriefingRoutePolicy route,
    TimeSpan? progressDrainTimeout = null,
    RetrievalSettings? retrieval = null) : IBriefingOrchestrator
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(1);

    public async Task<MemberBriefing> RunAsync(BriefingPlan plan, CancellationToken cancellationToken, IBriefingProgressSink? progress = null)
    {
        var workflow = BriefingWorkflowFactory.Create(sender, plan, route);
        var tracker = new BriefingProgressTracker(route.Route, retrieval);
        await using var pump = new BriefingProgressPump(progress ?? NullBriefingProgressSink.Instance, progressDrainTimeout ?? DefaultDrainTimeout);
        pump.Enqueue(tracker.Started());

        try
        {
            return await RunWorkflowAsync(workflow, plan, tracker, pump, cancellationToken);
        }
        catch (Exception) when (Report(pump, tracker.Failed(null)))
        {
            throw;
        }
    }

    private static async Task<MemberBriefing> RunWorkflowAsync(
        Workflow workflow,
        BriefingPlan plan,
        BriefingProgressTracker tracker,
        BriefingProgressPump pump,
        CancellationToken cancellationToken)
    {
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, plan, cancellationToken: cancellationToken);
        MemberBriefing? briefing = null;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken))
        {
            foreach (var step in tracker.Map(evt))
            {
                pump.Enqueue(step);
            }

            switch (evt)
            {
                case ExecutorFailedEvent { Data: Exception failure }:
                    Report(pump, tracker.Failed(evt));
                    Rethrow(failure);
                    break;
                case WorkflowErrorEvent { Data: Exception failure }:
                    Report(pump, tracker.Failed(evt));
                    Rethrow(failure);
                    break;
                case WorkflowOutputEvent { Data: MemberBriefing result }:
                    briefing = result;
                    break;
            }
        }

        // A cancelled run can end its event stream quietly; surface that as cancellation, not as a missing briefing.
        cancellationToken.ThrowIfCancellationRequested();
        return briefing ?? throw new InvalidOperationException("The briefing workflow finished without producing a briefing.");
    }

    // Also used as an exception filter, so the failure is reported without altering the original stack or exception.
    private static bool Report(BriefingProgressPump pump, BriefingProgress? failed)
    {
        if (failed is not null)
        {
            pump.Enqueue(failed);
        }

        return false;
    }

    private static void Rethrow(Exception failure)
    {
        var original = failure is AggregateException { InnerExceptions.Count: 1 } aggregate ? aggregate.InnerExceptions[0] : failure;
        ExceptionDispatchInfo.Capture(original).Throw();
    }
}
