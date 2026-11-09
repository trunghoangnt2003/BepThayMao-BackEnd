using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Persistence.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Default"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IPublicBranchReader, PublicBranchReader>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
