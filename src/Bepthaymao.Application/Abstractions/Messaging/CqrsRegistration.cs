using System.Reflection;
using Bepthaymao.Application.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Application.Abstractions.Messaging;

public static class CqrsRegistration
{
    public static IServiceCollection AddCqrsHandlers(this IServiceCollection services, Assembly assembly)
    {
        foreach (Type implementationType in assembly.GetTypes().Where(type =>
                     type is { IsAbstract: false, IsInterface: false }))
        {
            foreach (Type serviceType in implementationType.GetInterfaces().Where(IsHandlerInterface))
            {
                services.AddScoped(implementationType);
                services.AddScoped(serviceType, provider =>
                {
                    Type handlerType = serviceType.GetGenericTypeDefinition();
                    Type[] arguments = serviceType.GetGenericArguments();
                    object current = provider.GetRequiredService(implementationType);

                    if (handlerType == typeof(ICommandHandler<,>))
                    {
                        Type unitOfWorkType = typeof(UnitOfWorkCommandHandlerDecorator<,>)
                            .MakeGenericType(arguments);
                        current = ActivatorUtilities.CreateInstance(provider, unitOfWorkType, current);
                        Type validationType = typeof(ValidationCommandHandlerDecorator<,>)
                            .MakeGenericType(arguments);
                        current = ActivatorUtilities.CreateInstance(provider, validationType, current);
                        Type loggingType = typeof(LoggingCommandHandlerDecorator<,>)
                            .MakeGenericType(arguments);
                        return ActivatorUtilities.CreateInstance(provider, loggingType, current);
                    }

                    Type queryValidationType = typeof(ValidationQueryHandlerDecorator<,>)
                        .MakeGenericType(arguments);
                    current = ActivatorUtilities.CreateInstance(provider, queryValidationType, current);
                    Type queryLoggingType = typeof(LoggingQueryHandlerDecorator<,>)
                        .MakeGenericType(arguments);
                    return ActivatorUtilities.CreateInstance(provider, queryLoggingType, current);
                });
            }
        }

        return services;
    }

    private static bool IsHandlerInterface(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>);
    }
}
