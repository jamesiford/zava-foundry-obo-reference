using Compass.Mcp.ZavaApi.Application.Behaviors;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compass.Mcp.ZavaApi.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the sender, every handler and validator in this assembly, and the pipeline behaviors.
    /// </summary>
    /// <remarks>
    /// Handlers are discovered by scanning so adding a use case means adding classes, not editing this method
    /// (dev spec §6, Open/Closed). Behavior registration order is execution order, outermost first.
    /// </remarks>
    public static IServiceCollection AddZavaApiApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMessaging(assembly);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(IsValidatorContract))
            {
                services.AddScoped(contract, type);
            }
        }

        return services;
    }

    private static bool IsValidatorContract(Type contract) =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestValidator<>);
}
