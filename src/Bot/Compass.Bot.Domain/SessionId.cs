using System.Security.Cryptography;
using System.Text;

namespace Compass.Bot.Domain;

/// <summary>
/// The session identifier the bot sends to the hosted agents. Always minted here from the person's identity; there is
/// deliberately no way to build one from caller-supplied text, so a client can't ask to join someone else's
/// session (contract rule 8).
/// </summary>
public readonly record struct SessionId
{
    private SessionId(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Stable per person (so a person's conversation continues) and different for every other person. Derived from a
    /// hash rather than embedding the object ID, so the identifier is safe to log and to put on a header.
    /// </summary>
    public static SessionId Mint(PersonContext person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"compass|{person.TenantId}|{person.ObjectId}"));
        return new SessionId("compass-" + Convert.ToHexStringLower(hash.AsSpan(0, 16)));
    }

    public override string ToString() => Value;
}
