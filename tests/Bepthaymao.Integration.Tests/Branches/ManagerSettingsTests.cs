using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class ManagerSettingsTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private const string _settingsPath = "/api/v1/manager/branch/settings";

    private static readonly string[] _viewKeys =
    [
        "abandonedThresholdMinutes", "address", "bankAccountNo", "bankCode", "closeTime", "isActive", "name",
        "openTime", "phone", "sepayAccountRef", "zaloRecipientIds",
    ];

    private static object ValidSettings() => new
    {
        phone = "0912 345 678",
        openTime = "07:00",
        closeTime = "20:00",
        abandonedThresholdMinutes = 15,
        zaloRecipientIds = new[] { "zalo-a" },
    };

    private static async Task GiveBranchBankAccountAsync(AuthWorld world, Guid branchId, string name)
    {
        await world.WithDbAsync(async db =>
        {
            Branch branch = await db.Branches.SingleAsync(value => value.Id == branchId, TestContext.Current.CancellationToken);
            branch.ApplyOwnerDetails(new(name, "1 Đường Thử, Quận 1", "0901234567", 10.77, 106.7, new(6, 0), new(21, 30), 10, [],
                "0123456789", "VCB", "sepay-" + name, true));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
    }

    [Fact]
    public async Task ManagerReadsOwnBranchView()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        await GiveBranchBankAccountAsync(world, world.BranchA1, "Cơ sở A1");
        using HttpClient managerA1 = await world.ClientForAsync(AuthSubject.ManagerA1);
        using HttpClient managerA2 = await world.ClientForAsync(AuthSubject.ManagerA2);

        using HttpResponseMessage response = await managerA1.GetAsync(_settingsPath, TestContext.Current.CancellationToken);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Mong 200 nhưng nhận {(int)response.StatusCode}: {text}");
        using JsonDocument view = JsonDocument.Parse(text);
        Assert.Equal(_viewKeys, view.RootElement.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Cơ sở A1", view.RootElement.GetProperty("name").GetString());
        Assert.True(view.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal("0123456789", view.RootElement.GetProperty("bankAccountNo").GetString());
        Assert.Equal("VCB", view.RootElement.GetProperty("bankCode").GetString());
        Assert.Equal("sepay-Cơ sở A1", view.RootElement.GetProperty("sepayAccountRef").GetString());
        Assert.DoesNotContain(world.BranchA1.ToString(), text, StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage other = await managerA2.GetAsync(_settingsPath, TestContext.Current.CancellationToken);
        using JsonDocument otherView = JsonDocument.Parse(await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Cơ sở A2", otherView.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, otherView.RootElement.GetProperty("bankAccountNo").ValueKind);
    }

    [Fact]
    public async Task ManagerUpdatesAllowedFields()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        await GiveBranchBankAccountAsync(world, world.BranchA1, "Cơ sở A1");
        using HttpClient manager = await world.ClientForAsync(AuthSubject.ManagerA1);

        using HttpResponseMessage response = await manager.PutAsJsonAsync(_settingsPath, ValidSettings(), TestContext.Current.CancellationToken);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Mong 200 nhưng nhận {(int)response.StatusCode}: {text}");
        using JsonDocument saved = JsonDocument.Parse(text);
        AssertUpdated(saved.RootElement);

        using HttpResponseMessage again = await manager.GetAsync(_settingsPath, TestContext.Current.CancellationToken);
        using JsonDocument reread = JsonDocument.Parse(await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertUpdated(reread.RootElement);

        Branch stored = await world.WithDbAsync(db => db.Branches.AsNoTracking().SingleAsync(value => value.Id == world.BranchA1, TestContext.Current.CancellationToken));
        Assert.Equal(("0123456789", "VCB", "sepay-Cơ sở A1"), (stored.BankAccountNo, stored.BankCode, stored.SepayAccountRef));
        Assert.Equal(("Cơ sở A1", 10.77, 106.7, true), (stored.Name, stored.Lat, stored.Lng, stored.IsActive));
    }

    private static void AssertUpdated(JsonElement view)
    {
        Assert.Equal("0912345678", view.GetProperty("phone").GetString());
        Assert.Equal("07:00", view.GetProperty("openTime").GetString());
        Assert.Equal("20:00", view.GetProperty("closeTime").GetString());
        Assert.Equal(15, view.GetProperty("abandonedThresholdMinutes").GetInt32());
        Assert.Equal(["zalo-a"], view.GetProperty("zaloRecipientIds").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("Cơ sở A1", view.GetProperty("name").GetString());
        Assert.Equal("0123456789", view.GetProperty("bankAccountNo").GetString());
    }
}
