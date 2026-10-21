using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Queries;

/// <summary>
/// Asks the SharePoint retrieval agent for content, forwarding the person's Graph token. The session is minted by the
/// bot and passed in; no request type accepts one from the client (contract rule 8).
/// </summary>
public sealed record InvokeSharePointRetrievalAgentQuery(PersonContext Person, SessionId Session, AccessToken GraphToken, string Query)
    : IRequest<RetrievalResult>;
