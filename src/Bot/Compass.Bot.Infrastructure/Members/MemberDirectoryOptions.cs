namespace Compass.Bot.Infrastructure.Members;

/// <summary>Demo mapping from a person's Entra object ID to their Zava Credit Union member number.</summary>
public sealed class MemberDirectoryOptions
{
    public const string SectionName = "Bot:Members";

    public Dictionary<string, string> ByObjectId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
