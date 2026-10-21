namespace Compass.Agents.Contracts.Briefing;

/// <summary>The member's profile and balances as the Zava Credit Union API reported them to the calling person.</summary>
public sealed record MemberBriefingResult(
    string MemberNumber,
    string FullName,
    string Status,
    IReadOnlyList<AccountBriefing> Accounts,
    decimal NetBalance);

/// <summary>One account line of a <see cref="MemberBriefingResult"/>.</summary>
public sealed record AccountBriefing(string AccountNumber, string Type, string Nickname, decimal Balance, string Currency);
