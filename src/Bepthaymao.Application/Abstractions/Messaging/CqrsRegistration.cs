using System.Reflection;
using Bepthaymao.Application.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Application.Abstractions.Messaging;

public static class CqrsRegistration
{
    public static IServiceCollection AddCqrsHandlers(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (Type handler in assembly.GetTypes().Where(type => type is { IsAbstract: false, IsClass: true, ContainsGenericParameters: false }))
        {
            foreach (Type contract in handler.GetInterfaces().Where(IsHandlerContract))
            {
                services.AddScoped(handler);
                services.AddScoped(contract, provider => Decorate(provider, contract, handler));
            }
        }

        return services;
    }

    private static bool IsHandlerContract(Type type) => type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));

    private static object Decorate(IServiceProvider provider, Type contract, Type concrete)
    {
        Type[] arguments = contract.GetGenericArguments();
        object handler = provider.GetRequiredService(concrete);
        bool command = contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>);
        if (command)
        {
            handler = ActivatorUtilities.CreateInstance(provider,
                typeof(UnitOfWorkCommandHandlerDecorator<,>).MakeGenericType(arguments), handler);
        }

        handler = ActivatorUtilities.CreateInstance(provider,
            (command ? typeof(ValidationCommandHandlerDecorator<,>) : typeof(ValidationQueryHandlerDecorator<,>)).MakeGenericType(arguments), handler);
        return ActivatorUtilities.CreateInstance(provider,
            (command ? typeof(LoggingCommandHandlerDecorator<,>) : typeof(LoggingQueryHandlerDecorator<,>)).MakeGenericType(arguments), handler);
    }
}
