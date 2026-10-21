using Compass.Bot.Domain;

namespace Compass.Bot.Application.Abstractions;

/// <summary>
/// Maps the signed-in person to their member number in the Zava Credit Union system. A seam of its own because in a
/// real deployment this is a directory or CRM lookup; the demo backs it with configuration.
/// </summary>
public interface IMemberNumberResolver
{
    /// <exception cref="Exceptions.MemberNotLinkedException">The person has no member record.</exception>
    string Resolve(PersonContext person);
}
