using System.Text;
using Bepthaymao.Api.Hosting;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bepthaymao.Api.Security;

public static class PolicyNames
{
    public const string OwnerOnly = "OwnerOnly";
    public const string ManagerOnly = "ManagerOnly";
}

internal sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

internal static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt"))
            .Validate(options => BuildTimeDocs.IsActive || Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 UTF-8 bytes.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer) && !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt issuer and audience are required.")
            .Validate(options => options.AccessTokenMinutes == 15 && options.RefreshTokenDays == 7,
                "JWT lifetime must be 15 minutes and refresh lifetime 7 days.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                JwtOptions jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = BuildTimeDocs.IsActive ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(PolicyNames.OwnerOnly, policy => policy.RequireAuthenticatedUser().RequireRole("Owner").RequireClaim("owner_id"));
            options.AddPolicy(PolicyNames.ManagerOnly, policy => policy.RequireAuthenticatedUser().RequireRole("BranchManager").RequireClaim("branch_id"));
        });
        services.AddHttpContextAccessor();
        services.AddScoped<HttpCurrentUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<HttpCurrentUser>());
        services.AddScoped<IDataScope>(provider => provider.GetRequiredService<HttpCurrentUser>());
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        return services;
    }
}
