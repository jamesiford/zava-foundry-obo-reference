using Compass.Bot.Application.Abstractions;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Application.Commands;

/// <summary>Delegates to the broker; kept as its own handler so the exchange is a single, separately testable step.</summary>
public sealed class AcquireDownstreamTokenCommandHandler(IObOTokenBroker broker)
    : IRequestHandler<AcquireDownstreamTokenCommand, AccessToken>
{
    public Task<AccessToken> HandleAsync(AcquireDownstreamTokenCommand request, CancellationToken cancellationToken) =>
        broker.AcquireAsync(request.UserAssertion, request.Audience, cancellationToken);
}
