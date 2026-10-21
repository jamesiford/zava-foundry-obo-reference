using Compass.Agents.Contracts.Briefing;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>Asks the Zava API agent for the member's account picture, forwarding the person's Zava API token.</summary>
public sealed record InvokeZavaApiAgentQuery(PersonContext Person, SessionId Session, AccessToken ZavaApiToken, string MemberNumber)
    : IRequest<MemberBriefingResult>;
