using System.Globalization;
using Compass.Bot.Application.Models;
using Microsoft.Extensions.Configuration;

namespace Compass.Bot.Host.Readiness;

/// <summary>
/// Reads and validates <c>Bot:SharePoint:Retrieval:MinRelevance</c> and <c>:Summarise</c> in one place so startup and
/// readiness can't disagree. Blank means the default (no relevance cut, summarise on).
/// </summary>
public static class RetrievalSettingsReader
{
    /// <summary>The readiness label for a bad MinRelevance; names the key and what is accepted, never the value.</summary>
    public const string InvalidMinRelevanceLabel = $"{RetrievalSettings.MinRelevanceKey} (must be a number from 0 to 1)";

    /// <summary>The readiness label for a bad Summarise; names the key and what is accepted, never the value.</summary>
    public const string InvalidSummariseLabel = $"{RetrievalSettings.SummariseKey} (must be true or false)";

    /// <summary>Returns the configured settings, or null if either value is present but invalid.</summary>
    public static RetrievalSettings? TryRead(IConfiguration configuration) =>
        TryReadMinRelevance(configuration, out var min) && TryReadSummarise(configuration, out var summarise)
            ? new RetrievalSettings(min, summarise)
            : null;

    public static bool TryReadMinRelevance(IConfiguration configuration, out double minRelevance)
    {
        minRelevance = RetrievalSettings.HostDefault.MinRelevance;
        var raw = configuration[RetrievalSettings.MinRelevanceKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out minRelevance)
            && double.IsFinite(minRelevance)
            && minRelevance is >= 0 and <= 1;
    }

    public static bool TryReadSummarise(IConfiguration configuration, out bool summarise)
    {
        summarise = RetrievalSettings.HostDefault.Summarise;
        var raw = configuration[RetrievalSettings.SummariseKey];
        return string.IsNullOrWhiteSpace(raw) || bool.TryParse(raw.Trim(), out summarise);
    }

    /// <summary>Reads the settings at startup; an invalid value fails fast rather than silently running with a different one.</summary>
    public static RetrievalSettings Read(IConfiguration configuration) =>
        TryRead(configuration) ?? throw new InvalidOperationException($"{InvalidMinRelevanceLabel} or {InvalidSummariseLabel}.");
}
