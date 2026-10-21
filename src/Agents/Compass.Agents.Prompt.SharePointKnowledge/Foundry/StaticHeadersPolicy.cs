using System.ClientModel.Primitives;

namespace Compass.Agents.Prompt.SharePointKnowledge.Foundry;

/// <summary>
/// Stamps fixed headers on every request a single client sends. Scoped to one client instance — never ambient or
/// shared — so one person's headers can't leak onto another person's call (contract rule 2).
/// </summary>
internal sealed class StaticHeadersPolicy(IReadOnlyDictionary<string, string> headers) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Apply(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private void Apply(PipelineMessage message)
    {
        foreach (var (name, value) in headers)
        {
            message.Request.Headers.Set(name, value);
        }
    }
}
