using Bepthaymao.Api.Hosting;
using Microsoft.Extensions.Options;

namespace Bepthaymao.Api.Configuration;

/// <summary>Phase 1 only ships fakes for SePay and Zalo; any other mode must fail at startup.</summary>
internal sealed class IntegrationModeOptions
{
    public const string FakeMode = "Fake";
    private static readonly string[] _integrations = ["SePay", "Zalo"];

    public string Mode { get; set; } = FakeMode;

    public static IServiceCollection AddIntegrationModes(IServiceCollection services, IConfiguration configuration)
    {
        foreach (string name in _integrations)
        {
            OptionsBuilder<IntegrationModeOptions> options = services.AddOptions<IntegrationModeOptions>(name)
                .Bind(configuration.GetSection(name))
                .Validate(value => string.Equals(value.Mode, FakeMode, StringComparison.Ordinal),
                    $"Phase 1 chưa có adapter thật cho {name}; chỉ chấp nhận Fake");
            if (!BuildTimeDocs.IsActive)
            {
                options.ValidateOnStart();
            }
        }

        return services;
    }
}
