namespace Compass.Mcp.ZavaApi.Application.Models;

/// <summary>
/// A member's accounts plus the member-level <see cref="NetBalance"/> across them (loans count as negative).
/// </summary>
public sealed record MemberAccountsSummary(string MemberNumber, IReadOnlyList<AccountSummary> Accounts, decimal NetBalance);
