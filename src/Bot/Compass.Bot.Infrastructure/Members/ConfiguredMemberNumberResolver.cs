using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Domain;
using Microsoft.Extensions.Options;

namespace Compass.Bot.Infrastructure.Members;

/// <summary>
/// Configuration-backed <see cref="IMemberNumberResolver"/>. Keyed on the object ID taken from the validated token, so
/// the member number can never come from what the person typed.
/// </summary>
public sealed class ConfiguredMemberNumberResolver(IOptions<MemberDirectoryOptions> options) : IMemberNumberResolver
{
    private readonly MemberDirectoryOptions _options = options.Value;

    public string Resolve(PersonContext person)
    {
        ArgumentNullException.ThrowIfNull(person);

        // Linux App Service rejects hyphens in setting names, so deployed entries are keyed on the object id without them.
        var memberNumber = _options.ByObjectId.TryGetValue(person.ObjectId, out var exact)
            ? exact
            : _options.ByObjectId.GetValueOrDefault(person.ObjectId.Replace("-", string.Empty, StringComparison.Ordinal));

        return !string.IsNullOrWhiteSpace(memberNumber) ? memberNumber : throw new MemberNotLinkedException();
    }
}
