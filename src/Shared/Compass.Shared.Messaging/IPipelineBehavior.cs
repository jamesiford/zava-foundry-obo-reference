namespace Compass.Shared.Messaging;

/// <summary>Invokes the next step of the pipeline: either another behavior or the handler itself.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting concern (logging, validation) applied once around every handler rather than repeated inside each
/// one (dev spec §6, DRY).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
