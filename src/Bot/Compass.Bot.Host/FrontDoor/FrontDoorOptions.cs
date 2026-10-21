namespace Compass.Bot.Host.FrontDoor;

/// <summary>Settings for the web front door (<c>Bot:FrontDoor</c>): the sign-in page and the API behind it.</summary>
public sealed class FrontDoorOptions
{
    public const string SectionName = "Bot:FrontDoor";

    /// <summary>
    /// Serves the page and maps <c>POST /api/briefing</c>. On by default so a fresh deployment can be exercised without
    /// Teams; set <c>Bot:FrontDoor:Enabled=false</c> (app setting <c>Bot__FrontDoor__Enabled</c>) to remove both.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Longest accepted question, in characters, after trimming. Bounds what a caller can push into the model.</summary>
    public int MaxQuestionLength { get; set; } = 500;

    /// <summary>Largest accepted request body. A question is a sentence; anything bigger is not a question.</summary>
    public int MaxBodyBytes { get; set; } = 4096;

    /// <summary>How long one briefing may run before the call ends with a timeout, matching the Teams deadline.</summary>
    public TimeSpan TurnDeadline { get; set; } = TimeSpan.FromSeconds(110);
}
