using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Branches;
using Bepthaymao.Application.Branches.ListPublicBranches;
using Bepthaymao.Application.Branches.Owner;
using Bepthaymao.Application.Menu;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Overview;
using Bepthaymao.Application.Shop;
using Bepthaymao.Infrastructure.Media;
using Bepthaymao.Infrastructure.Payments;
using Bepthaymao.Infrastructure.Persistence.Menu;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Infrastructure.Persistence.Branches;
using Bepthaymao.Infrastructure.Persistence.Orders;
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
        services.AddScoped<IMenuStore, MenuStore>();
        services.AddScoped<IShopStore, ShopStore>();
        services.AddScoped<ILegalStore, LegalStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderAccessGrants, OrderAccessGrantService>();
        services.AddScoped<IOverviewReader, OverviewReader>();
        services.AddSingleton<IPaymentQrService, VietQrService>();
        services.AddSingleton<IDishImageProcessor, SkiaDishImageProcessor>();
        services.AddSingleton<IDishImageStorage, FileSystemDishImageStorage>();
        services.AddSingleton(_ => ResolveDishImageStorage(configuration));
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
        services.AddScoped<Seeding.MenuSampleSeeder>();
        return services;
    }

    /// <summary>Thiếu <c>DishImages:StoragePath</c> thì ném khi host khởi động (Api resolve lúc map static files); đường dẫn tương đối tính từ content root (khóa host "contentRoot").</summary>
    private static DishImageStorageOptions ResolveDishImageStorage(IConfiguration configuration)
    {
        string? path = configuration["DishImages:StoragePath"];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Thiếu cấu hình DishImages:StoragePath (thư mục lưu ảnh món).");
        return new(Path.GetFullPath(Path.Combine(configuration["contentRoot"] ?? Directory.GetCurrentDirectory(), path)));
    }
}
