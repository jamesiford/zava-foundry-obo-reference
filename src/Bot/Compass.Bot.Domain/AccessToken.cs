namespace Compass.Bot.Domain;

/// <summary>
/// A bearer token held only in memory for the length of one turn. Exists so a token can never be printed by accident:
/// <see cref="ToString"/> and the record's generated printing show a placeholder, and the raw value is reachable only
/// through the deliberately named <see cref="Reveal"/> (contract rule 2).
/// </summary>
public sealed record AccessToken
{
    private readonly string _value;

    public AccessToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    /// <summary>Returns the raw token. Call only at the point of use, i.e. when putting it on an outbound request.</summary>
    public string Reveal() => _value;

    public override string ToString() => "[AccessToken]";

    // Record equality would compare the secret; reference equality is enough and avoids timing-sensitive comparisons.
    public bool Equals(AccessToken? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Value = [redacted]");
        return true;
    }
}
