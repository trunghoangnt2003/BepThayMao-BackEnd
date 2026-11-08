using Microsoft.AspNetCore.HttpOverrides;

namespace Bepthaymao.Api.Security;

internal static class ForwardedHeadersSetup
{
    public static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            foreach (string network in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }
}
