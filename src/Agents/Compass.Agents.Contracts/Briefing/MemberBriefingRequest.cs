namespace Compass.Agents.Contracts.Briefing;

/// <summary>Ask the Zava API agent for the account picture of one member.</summary>
public sealed record MemberBriefingRequest(string MemberNumber);
