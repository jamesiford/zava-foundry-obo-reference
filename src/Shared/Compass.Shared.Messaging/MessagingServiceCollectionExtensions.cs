using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Compass.Shared.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sender, the logging behavior and every <see cref="IRequestHandler{TRequest,TResponse}"/> found in
    /// <paramref name="handlerAssemblies"/>.
    /// </summary>
    /// <remarks>
    /// Handlers are discovered by scanning so adding a use case means adding a class, not editing a registration list
    /// (dev spec §6, Open/Closed). Behaviors registered after this call run inside the logging behavior.
    /// </remarks>
    public static IServiceCollection AddMessaging(this IServiceCollection services, params Assembly[] handlerAssemblies)
    {
        services.TryAddScoped<ISender, Sender>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        foreach (var type in handlerAssemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(IsHandlerContract))
            {
                services.AddScoped(contract, type);
            }
        }

        return services;
    }

    private static bool IsHandlerContract(Type contract) =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>);
}
