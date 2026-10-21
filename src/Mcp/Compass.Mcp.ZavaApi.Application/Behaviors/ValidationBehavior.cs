using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Validation;

namespace Compass.Mcp.ZavaApi.Application.Behaviors;

/// <summary>
/// Runs every registered <see cref="IRequestValidator{TRequest}"/> before the handler, so no handler (and no
/// repository query) ever sees malformed input.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IRequestValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return next();
    }
}
