using System.Reflection;
using Bepthaymao.Application.Abstractions.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        Assembly assembly = typeof(DependencyInjection).Assembly;
        services.AddCqrsHandlers(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
