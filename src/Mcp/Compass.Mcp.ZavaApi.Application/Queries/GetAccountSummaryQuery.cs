using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

/// <summary>Account balances for a member.</summary>
public sealed record GetAccountSummaryQuery(string MemberNumber) : IRequest<MemberAccountsSummary>;
