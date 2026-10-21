using Compass.Agents.Contracts.Knowledge;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// Asks the SharePoint knowledge agent for a grounded answer, forwarding the person's Search token. The session is
/// minted by the bot and passed in (contract rule 8).
/// </summary>
public sealed record InvokeSharePointKnowledgeAgentQuery(PersonContext Person, SessionId Session, AccessToken SearchToken, string Question)
    : IRequest<KnowledgeQueryResult>;
