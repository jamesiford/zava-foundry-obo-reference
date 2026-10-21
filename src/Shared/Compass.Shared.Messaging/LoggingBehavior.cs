using System.Diagnostics;
using Compass.Shared.Messaging;
using Microsoft.Extensions.Logging;

namespace Compass.Shared.Messaging;

/// <summary>
/// Logs one line per use case with its duration. Deliberately logs the request <em>type</em> only, never its
/// payload, so member identifiers and anything token-adjacent can't leak through a generic behavior.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();
            logger.LogInformation("Handled {Request} in {ElapsedMs} ms", requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            // Type only: exception messages can echo caller-supplied values.
            logger.LogWarning("{Request} failed with {ExceptionType} after {ElapsedMs} ms", requestName, ex.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
