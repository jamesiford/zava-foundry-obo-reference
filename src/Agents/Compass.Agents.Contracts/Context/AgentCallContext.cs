using System.Text;

namespace Compass.Agents.Contracts.Context;

/// <summary>
/// What already arrived in the request headers for one agent call: the person's object ID and the downstream
/// tokens the bot obtained on their behalf.
/// </summary>
/// <remarks>
/// Lives for exactly one call and is never cached or serialised. <see cref="ToString"/> and the generated
/// record printing deliberately list header <em>names</em> only, so an accidental
/// <c>logger.LogInformation("{Context}", context)</c> can't leak a bearer token (contract rule 2).
/// </remarks>
public sealed record AgentCallContext(string PersonObjectId, IReadOnlyDictionary<string, string> ForwardedTokens)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"AgentCallContext {{ PersonObjectId = {PersonObjectId}, ForwardedTokens = [{string.Join(", ", ForwardedTokens.Keys)}] }}";

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PersonObjectId = ").Append(PersonObjectId)
            .Append(", ForwardedTokens = [").Append(string.Join(", ", ForwardedTokens.Keys)).Append(']');
        return true;
    }
}
