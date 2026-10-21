using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;

namespace Compass.Bot.Host.Turns;

/// <summary>
/// <see cref="IReplyStream"/> over the SDK's per-turn <see cref="IStreamingResponse"/>.
/// </summary>
/// <remarks>
/// The SDK hands out exactly one <c>StreamingResponse</c> per <see cref="ITurnContext"/>, which is what makes "one stream
/// per turn" structural. It queues chunks and sends them on a timer, so this class adds what the SDK does not promise:
/// every call is awaited under one gate (no overlapping sends), and once the stream is ended nothing more is sent to it.
/// </remarks>
public sealed class SdkReplyStream(IStreamingResponse stream, StreamingOptions options) : IReplyStream
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ended;

    public bool IsStarted => stream.IsStreamStarted();

    public async Task InformAsync(string text, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ended)
            {
                return;
            }

            await stream.QueueInformativeUpdateAsync(text, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StreamEndResult> EndAsync(string text, IReadOnlyList<ReplyCitation> citations, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ended)
            {
                return StreamEndResult.Delivered;
            }

            // Set before the call: even if ending fails, nothing may be sent to this stream again.
            _ended = true;

            // Citations ride in the stream's own final message rather than a separate activity, which Teams may
            // otherwise render before or inside the stream.
            // The SDK's Citation needs a link; a source with no web link stays in the answer text only.
            var cited = citations.Where(c => c.Url is not null).Take(options.MaxCitations).ToList();
            for (var i = 0; i < cited.Count; i++)
            {
                stream.AddCitation(new Citation(cited[i].Title, cited[i].Title, cited[i].Url!), i + 1);
            }

            stream.QueueTextChunk(cited.Count == 0 ? text : $"{text}\n\nReferences: {string.Join(' ', cited.Select((_, i) => $"[{i + 1}]"))}");

            return await stream.EndStreamAsync(cancellationToken) switch
            {
                StreamingResponseResult.Success or StreamingResponseResult.AlreadyEnded => StreamEndResult.Delivered,
                StreamingResponseResult.UserCancelled => StreamEndResult.UserCancelled,
                _ => StreamEndResult.NotDelivered,
            };
        }
        finally
        {
            _gate.Release();
        }
    }
}
