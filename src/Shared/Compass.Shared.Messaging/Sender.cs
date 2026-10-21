using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Shared.Messaging;

/// <summary>
/// Resolves the handler and pipeline behaviors for a request from DI and runs them.
/// </summary>
/// <remarks>
/// Hand-rolled instead of MediatR because MediatR's current major versions require a commercial license key
/// (dev spec D10). Scoped, so handlers and the EF Core context they use share the invocation's lifetime.
/// </remarks>
internal sealed class Sender(IServiceProvider services) : ISender
{
    // Closed-generic wrappers are built once per request type; reflection stays off the hot path.
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Wrappers = new();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd((request.GetType(), typeof(TResponse)), static key =>
        {
            var wrapperType = typeof(RequestWrapper<,>).MakeGenericType(key.Request, key.Response);
            return Activator.CreateInstance(wrapperType)!;
        });

        return wrapper.HandleAsync(request, services, cancellationToken);
    }

    private abstract class RequestWrapper<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestWrapper<TRequest, TResponse> : RequestWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;
            var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>();

            RequestHandlerDelegate<TResponse> pipeline = () => handler.HandleAsync(typedRequest, cancellationToken);

            // Registration order is outermost-first, so fold from the end.
            foreach (var behavior in behaviors.Reverse())
            {
                var next = pipeline;
                pipeline = () => behavior.HandleAsync(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
