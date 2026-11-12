using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Bepthaymao.Application.Branches.Owner;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Persistence.Branches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

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
        services.AddScoped<IOwnerBranchReader, OwnerBranchReader>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddIdentityCore<AppUser>(options =>
        {
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
            options.User.RequireUniqueEmail = false;
            options.Lockout.AllowedForNewUsers = false;
        }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IUserAuthenticator, IdentityUserAuthenticator>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IManagerAccountService, ManagerAccountService>();
        services.AddScoped<Seeding.AccountSeeder>();
        return services;
    }
}
