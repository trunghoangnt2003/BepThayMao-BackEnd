using Microsoft.Extensions.Options;

namespace Bepthaymao.Api.Configuration;

internal sealed class IntegrationModeOptions
{
    public string Mode { get; set; } = "Fake";
}

internal static class IntegrationModeSetup
{
    public static IServiceCollection AddIntegrationModes(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (string name in new[] { "SePay", "Zalo" })
        {
            OptionsBuilder<IntegrationModeOptions> options = services.AddOptions<IntegrationModeOptions>(name)
                .Bind(configuration.GetSection(name))
                .Validate(value => value.Mode is "Fake" or "Live", "Mode must be Fake or Live.");
            if (!Hosting.BuildTimeDocs.IsActive)
            {
                options.ValidateOnStart();
            }
        }
        return services;
    }
}
