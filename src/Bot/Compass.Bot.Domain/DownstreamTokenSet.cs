namespace Compass.Bot.Domain;

/// <summary>
/// The person's downstream tokens for a single turn. Never persisted or cached by design: it is built, used and
/// dropped within the turn, so a later turn always re-exchanges with the identity provider (contract rule 2).
/// </summary>
public sealed class DownstreamTokenSet
{
    private readonly Dictionary<DownstreamAudience, AccessToken> _tokens = [];

    public DownstreamTokenSet Add(DownstreamAudience audience, AccessToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!_tokens.TryAdd(audience, token))
        {
            throw new InvalidOperationException($"A token for {audience} was already acquired this turn; exchange once per audience.");
        }

        return this;
    }

    public AccessToken Get(DownstreamAudience audience) =>
        _tokens.TryGetValue(audience, out var token)
            ? token
            : throw new InvalidOperationException($"No token was acquired for {audience}.");

    public override string ToString() => $"DownstreamTokenSet({string.Join(", ", _tokens.Keys)})";
}
