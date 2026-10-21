namespace Compass.Shared.Messaging;

/// <summary>
/// Marker for a request that yields <typeparamref name="TResponse"/>. Shaped like MediatR's <c>IRequest&lt;T&gt;</c>
/// so a later swap to MediatR or the <c>Mediator</c> source generator is a namespace change, not a redesign (dev spec D10).
/// </summary>
public interface IRequest<out TResponse>;
