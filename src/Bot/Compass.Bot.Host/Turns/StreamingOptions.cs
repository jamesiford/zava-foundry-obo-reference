namespace Compass.Bot.Host.Turns;

/// <summary>Limits for streaming a turn. Defaults follow the Teams streaming guidance; tests shorten the interval.</summary>
public sealed record StreamingOptions
{
    /// <summary>Teams throttles stream updates to about one per second; faster ones are dropped or rejected.</summary>
    public TimeSpan MinUpdateInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Informative text is capped well below the channel's limit.</summary>
    public int MaxInformativeLength { get; init; } = 1000;

    /// <summary>A Teams stream times out after about two minutes, so the turn stops a little before that.</summary>
    public TimeSpan TurnDeadline { get; init; } = TimeSpan.FromSeconds(110);

    /// <summary>Citations per answer; more than this is noise in the Teams reference list.</summary>
    public int MaxCitations { get; init; } = 10;
}
