using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace Compass.Bot.Host.Turns;

/// <summary>
/// Delivers one turn to the person: streamed in a 1:1 chat, a single message elsewhere.
/// </summary>
/// <remarks>
/// Ordering rules from Microsoft's custom engine agent guidance, kept in one place:
/// one stream per turn; the final message and citations go inside that stream; the stream is ended before anything else
/// is sent, and nothing is sent to it afterwards; every send is awaited; a follow-up after the stream carries
/// <c>replyToId</c> so it stays in the same thread. Group chats and channels do not support streaming, so they get one
/// interim message and a typing indicator, then the answer.
/// </remarks>
public sealed class TeamsTurnResponder(
    BriefingTurnHandler handler,
    IReplyStreamFactory streams,
    StreamingOptions options,
    TimeProvider time,
    ILogger<TeamsTurnResponder> logger)
{
    public const string WorkingMessage = "Working on your briefing. I'll reply here shortly.";
    public const string TimedOutMessage = "That is taking longer than expected, so I stopped. Please try again.";
    public const string FailedMessage = "Something went wrong while preparing your briefing. Please try again.";

    private const string PersonalConversation = "personal";

    public Task RespondAsync(ITurnContext turnContext, string? ssoToken, CancellationToken cancellationToken) =>
        IsOneToOne(turnContext)
            ? RespondStreamingAsync(turnContext, ssoToken, cancellationToken)
            : RespondWithInterimAsync(turnContext, ssoToken, cancellationToken);

    // Streaming is a 1:1 Teams feature; reading StreamingResponse only here keeps group turns from creating one.
    private static bool IsOneToOne(ITurnContext turnContext) =>
        string.Equals(turnContext.Activity.Conversation?.ConversationType, PersonalConversation, StringComparison.OrdinalIgnoreCase)
        && turnContext.StreamingResponse.IsStreamingChannel;

    private async Task RespondStreamingAsync(ITurnContext turnContext, string? ssoToken, CancellationToken cancellationToken)
    {
        var stream = streams.Create(turnContext);
        await using var sink = new StreamingProgressSink(stream, turnContext.Activity.From?.Name, options, time, logger);

        TurnReply reply;
        try
        {
            reply = await RunAsync(turnContext, ssoToken, sink, cancellationToken);
        }
        catch (Exception) when (stream.IsStarted && !cancellationToken.IsCancellationRequested)
        {
            // An open stream must be finalised even when the turn blew up, or Teams shows it spinning until it times out.
            await sink.CloseAsync();
            await FinishAsync(turnContext, stream, new TurnReply(FailedMessage), cancellationToken);
            throw;
        }

        // From here on no update may reach the stream: close the sink before the final message is queued.
        await sink.CloseAsync();
        await FinishAsync(turnContext, stream, reply, cancellationToken);
    }

    private async Task FinishAsync(ITurnContext turnContext, IReplyStream stream, TurnReply reply, CancellationToken cancellationToken)
    {
        // A refused or never-started stream is not opened now just to close it: one plain reply is the fallback.
        if (!stream.IsStarted)
        {
            await SendReplyAsync(turnContext, reply.Text, cancellationToken);
            return;
        }

        StreamEndResult result;
        try
        {
            result = await stream.EndAsync(reply.Text, reply.Citations, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Ending the stream failed ({ExceptionType}); sending the answer as a follow-up", ex.GetType().Name);
            result = StreamEndResult.NotDelivered;
        }

        // The only thing allowed after the stream is a follow-up in the same thread, and never after the person pressed stop.
        if (result == StreamEndResult.NotDelivered)
        {
            await SendReplyAsync(turnContext, reply.Text, cancellationToken);
        }
    }

    private async Task RespondWithInterimAsync(ITurnContext turnContext, string? ssoToken, CancellationToken cancellationToken)
    {
        await SendReplyAsync(turnContext, WorkingMessage, cancellationToken);
        await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing, ReplyToId = turnContext.Activity.Id }, cancellationToken);

        var reply = await RunAsync(turnContext, ssoToken, progress: null, cancellationToken);
        await SendReplyAsync(turnContext, reply.Text, cancellationToken);
    }

    // Both paths share the two-minute budget; running over it gives the person an answer instead of a stuck chat.
    private async Task<TurnReply> RunAsync(ITurnContext turnContext, string? ssoToken, StreamingProgressSink? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.TurnDeadline);
        try
        {
            return await handler.HandleAsync(ssoToken, turnContext.Activity.Text, deadline.Token, progress);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Turn stopped after the {Deadline} deadline", options.TurnDeadline);
            return new TurnReply(TimedOutMessage);
        }
    }

    private static Task SendReplyAsync(ITurnContext turnContext, string text, CancellationToken cancellationToken)
    {
        var message = MessageFactory.Text(text);
        message.ReplyToId = turnContext.Activity.Id;
        return turnContext.SendActivityAsync(message, cancellationToken);
    }
}
