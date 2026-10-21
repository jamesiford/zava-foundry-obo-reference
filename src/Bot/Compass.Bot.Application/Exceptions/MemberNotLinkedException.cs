namespace Compass.Bot.Application.Exceptions;

/// <summary>The signed-in person has no linked Zava Credit Union member record.</summary>
public sealed class MemberNotLinkedException() : Exception("No member record is linked to the signed-in person.");
