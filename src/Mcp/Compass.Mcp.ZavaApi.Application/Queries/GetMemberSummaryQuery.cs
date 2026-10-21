using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

/// <summary>Member profile lookup by member number.</summary>
public sealed record GetMemberSummaryQuery(string MemberNumber) : IRequest<MemberSummary>;
