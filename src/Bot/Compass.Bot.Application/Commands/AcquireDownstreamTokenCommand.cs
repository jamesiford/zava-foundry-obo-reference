using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Commands;

/// <summary>
/// One on-behalf-of exchange for one downstream audience. The returned <see cref="AccessToken"/> is request-scoped:
/// the caller uses it and drops it (contract rule 2).
/// </summary>
public sealed record AcquireDownstreamTokenCommand(AccessToken UserAssertion, DownstreamAudience Audience) : IRequest<AccessToken>;
