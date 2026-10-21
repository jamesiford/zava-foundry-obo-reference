namespace Compass.Shared.Messaging;

/// <summary>
/// Handles exactly one request type. One handler per use case keeps each class to a single reason to change
/// (dev spec §6, Single Responsibility).
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
