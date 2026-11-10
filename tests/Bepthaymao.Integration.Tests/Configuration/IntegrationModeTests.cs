using System.Net;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Bepthaymao.Integration.Tests.Configuration;

public sealed class IntegrationModeTests
{
    [Fact]
    public async Task DefaultFakeModesStart()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("SePay")]
    [InlineData("Zalo")]
    public async Task LiveModeFailsStartupWithClearMessage(string integration)
    {
        await using ApiFactory factory = new(new Dictionary<string, string?> { [$"{integration}:Mode"] = "Live" });
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains($"Phase 1 chưa có adapter thật cho {integration}; chỉ chấp nhận Fake", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingConnectionStringFailsStartupWithoutPrintingValues()
    {
        await using ApiFactory factory = new(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "" });
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Redis", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }
}
