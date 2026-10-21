namespace Compass.Shared.Messaging;

/// <summary>
/// The only thing the Host depends on to run a use case, so triggers stay thin: parse input, send, map the result.
/// </summary>
public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken);
}
