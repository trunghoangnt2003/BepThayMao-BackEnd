using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Persistence.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bepthaymao.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Read lazily: build-time OpenAPI generation starts the host without any database configured.
        services.AddDbContext<AppDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IPublicBranchReader, PublicBranchReader>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
