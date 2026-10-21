namespace Compass.Bot.Host.Turns;

/// <summary>A source shown with the answer. Title and link only: the retrieved passage itself stays in the answer text.</summary>
public sealed record ReplyCitation(string Title, string? Url);

/// <summary>How the final message of a stream ended up.</summary>
public enum StreamEndResult
{
    /// <summary>The final message and its citations went out inside the stream.</summary>
    Delivered,

    /// <summary>The person pressed stop. Nothing more may be sent for this turn.</summary>
    UserCancelled,

    /// <summary>The channel did not accept the final message; the caller may send one follow-up.</summary>
    NotDelivered,
}

/// <summary>
/// The one stream a 1:1 turn owns. A port over the Agents SDK <c>StreamingResponse</c> so the ordering rules Teams
/// imposes (one stream per turn, the final message and citations inside it, nothing after it ends) live in code that can
/// be tested without a channel.
/// </summary>
public interface IReplyStream
{
    /// <summary>Whether the channel has accepted the first update, i.e. a stream is open and must be finalised.</summary>
    bool IsStarted { get; }

    /// <summary>Sends an informative update. A no-op once the stream has ended; throws if the channel refuses it.</summary>
    Task InformAsync(string text, CancellationToken cancellationToken);

    /// <summary>Sends the final message with its citations and ends the stream. Safe to call twice; the second call does nothing.</summary>
    Task<StreamEndResult> EndAsync(string text, IReadOnlyList<ReplyCitation> citations, CancellationToken cancellationToken);
}
