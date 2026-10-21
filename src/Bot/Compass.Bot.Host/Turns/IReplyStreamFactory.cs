using Microsoft.Agents.Builder;

namespace Compass.Bot.Host.Turns;

/// <summary>Opens the one <see cref="IReplyStream"/> of a turn. A seam so the ordering logic can be driven by a fake channel.</summary>
public interface IReplyStreamFactory
{
    IReplyStream Create(ITurnContext turnContext);
}

/// <summary>Wraps the SDK's per-turn <c>StreamingResponse</c>; asking twice for the same turn yields the same underlying stream.</summary>
public sealed class SdkReplyStreamFactory(StreamingOptions options) : IReplyStreamFactory
{
    public IReplyStream Create(ITurnContext turnContext) => new SdkReplyStream(turnContext.StreamingResponse, options);
}
