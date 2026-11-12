using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class CreateBranchTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private const string _managerPassword = "mat-khau-quan-ly-12";

    internal static object ValidBody(string managerIdentifier) => new
    {
        branch = new
        {
            name = "Cơ sở mới",
            address = "12 Nguyễn Huệ, Quận 1",
            phone = "0901 234 567",
            lat = 10.7769,
            lng = 106.7009,
            openTime = "06:00",
            closeTime = "21:30",
            abandonedThresholdMinutes = 10,
            zaloRecipientIds = new[] { "zalo-1" },
            isActive = true,
        },
        manager = new { identifier = managerIdentifier, password = _managerPassword },
    };

    [Fact]
    public async Task OwnerCreatesBranchAndManagerLogsInImmediately()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpResponseMessage created = await owner.PostAsJsonAsync("/api/v1/owner/branches", ValidBody("quanly-moi"), TestContext.Current.CancellationToken);
        string createdText = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"Mong 201 nhưng nhận {(int)created.StatusCode}: {createdText}");
        using JsonDocument body = JsonDocument.Parse(createdText);
        Guid branchId = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/owner/branches/{branchId}", created.Headers.Location?.OriginalString);
        Assert.Equal("quanly-moi", body.RootElement.GetProperty("managerIdentifier").GetString());
        Assert.DoesNotContain("password", createdText, StringComparison.OrdinalIgnoreCase);

        using HttpClient anonymous = world.Factory.CreateClient();
        using HttpResponseMessage login = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = "quanly-moi", password = _managerPassword }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using JsonDocument tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("BranchManager", tokens.RootElement.GetProperty("role").GetString());
        JsonWebToken jwt = new(tokens.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(branchId.ToString(), jwt.GetClaim("branch_id").Value);
    }
}
