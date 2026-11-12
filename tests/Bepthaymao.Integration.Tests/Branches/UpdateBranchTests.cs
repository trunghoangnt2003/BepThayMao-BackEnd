using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class UpdateBranchTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private static readonly string[] _publicKeys = ["address", "closeTime", "id", "name", "openTime", "phone"];

    private static JsonObject FullBody(bool isActive = true) => new()
    {
        ["name"] = "Cơ sở A1 mới",
        ["address"] = "99 Lê Lợi, Quận 1",
        ["phone"] = "0987 654 321",
        ["lat"] = 10.8,
        ["lng"] = 106.8,
        ["openTime"] = "07:30",
        ["closeTime"] = "22:00",
        ["abandonedThresholdMinutes"] = 20,
        ["zaloRecipientIds"] = new JsonArray("z1", "z2"),
        ["isActive"] = isActive,
        ["bankAccountNo"] = "111222333",
        ["bankCode"] = "ACB",
        ["sepayAccountRef"] = "sepay-xyz",
    };

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expected, $"Mong {(int)expected} nhưng nhận {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private static void AssertUpdated(JsonElement branch, Guid id)
    {
        Assert.Equal(id, branch.GetProperty("id").GetGuid());
        Assert.Equal("Cơ sở A1 mới", branch.GetProperty("name").GetString());
        Assert.Equal("99 Lê Lợi, Quận 1", branch.GetProperty("address").GetString());
        Assert.Equal("0987654321", branch.GetProperty("phone").GetString());
        Assert.Equal(10.8, branch.GetProperty("lat").GetDouble());
        Assert.Equal(106.8, branch.GetProperty("lng").GetDouble());
        Assert.Equal("07:30", branch.GetProperty("openTime").GetString());
        Assert.Equal("22:00", branch.GetProperty("closeTime").GetString());
        Assert.Equal(20, branch.GetProperty("abandonedThresholdMinutes").GetInt32());
        Assert.Equal(["z1", "z2"], branch.GetProperty("zaloRecipientIds").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("111222333", branch.GetProperty("bankAccountNo").GetString());
        Assert.Equal("ACB", branch.GetProperty("bankCode").GetString());
        Assert.Equal("sepay-xyz", branch.GetProperty("sepayAccountRef").GetString());
        Assert.Equal("manager-a1", branch.GetProperty("managerIdentifier").GetString());
    }

    [Fact]
    public async Task OwnerUpdatesAllFields()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        string path = $"/api/v1/owner/branches/{world.BranchA1}";

        using StringContent content = Json(FullBody());
        using HttpResponseMessage response = await owner.PutAsync(path, content, TestContext.Current.CancellationToken);
        using JsonDocument saved = await ReadAsync(response, HttpStatusCode.OK);
        AssertUpdated(saved.RootElement, world.BranchA1);

        using HttpResponseMessage again = await owner.GetAsync(path, TestContext.Current.CancellationToken);
        using JsonDocument reread = await ReadAsync(again, HttpStatusCode.OK);
        AssertUpdated(reread.RootElement, world.BranchA1);
    }

    [Fact]
    public async Task DeactivatedBranchDisappearsFromPublicListAtOnce()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpClient anonymous = await world.ClientForAsync(AuthSubject.Anonymous);
        string path = $"/api/v1/owner/branches/{world.BranchA1}";

        Assert.Equal(3, (await PublicBranchesAsync(anonymous)).RootElement.GetArrayLength());

        using StringContent off = Json(FullBody(isActive: false));
        using HttpResponseMessage deactivated = await owner.PutAsync(path, off, TestContext.Current.CancellationToken);
        using JsonDocument offBranch = await ReadAsync(deactivated, HttpStatusCode.OK);
        Assert.False(offBranch.RootElement.GetProperty("isActive").GetBoolean());

        using JsonDocument listed = await PublicBranchesAsync(anonymous);
        Assert.Equal(2, listed.RootElement.GetArrayLength());
        Assert.DoesNotContain(listed.RootElement.EnumerateArray(), item => item.GetProperty("id").GetGuid() == world.BranchA1);
        Assert.All(listed.RootElement.EnumerateArray(),
            item => Assert.Equal(_publicKeys, item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.True(await world.WithDbAsync(db => db.Branches.AnyAsync(branch => branch.Id == world.BranchA1, TestContext.Current.CancellationToken)));
        Assert.True(await world.WithDbAsync(db => db.Users.AnyAsync(user => user.BranchId == world.BranchA1, TestContext.Current.CancellationToken)));
        using HttpClient managerStillLogsIn = await world.ClientForAsync(AuthSubject.ManagerA1);

        using StringContent on = Json(FullBody(isActive: true));
        using HttpResponseMessage reactivated = await owner.PutAsync(path, on, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        using JsonDocument relisted = await PublicBranchesAsync(anonymous);
        Assert.Equal(3, relisted.RootElement.GetArrayLength());
        Assert.Contains(relisted.RootElement.EnumerateArray(), item => item.GetProperty("id").GetGuid() == world.BranchA1);
        Assert.All(relisted.RootElement.EnumerateArray(),
            item => Assert.Equal(_publicKeys, item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ForeignOrInvalidUpdate()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);

        using StringContent foreign = Json(FullBody());
        using HttpResponseMessage notMine = await owner.PutAsync($"/api/v1/owner/branches/{world.BranchB1}", foreign, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);

        JsonObject backwards = FullBody();
        backwards["openTime"] = "22:00";
        backwards["closeTime"] = "07:30";
        using StringContent backwardsContent = Json(backwards);
        using HttpResponseMessage badHours = await owner.PutAsync($"/api/v1/owner/branches/{world.BranchA1}", backwardsContent, TestContext.Current.CancellationToken);
        using JsonDocument hoursProblem = await ReadAsync(badHours, HttpStatusCode.BadRequest);
        Assert.True(hoursProblem.RootElement.GetProperty("errors").TryGetProperty("openTime", out _), "Thiếu khóa lỗi openTime.");

        JsonObject stranger = FullBody();
        stranger["ownerId"] = Guid.CreateVersion7();
        using StringContent strangerContent = Json(stranger);
        using HttpResponseMessage unknownMember = await owner.PutAsync($"/api/v1/owner/branches/{world.BranchA1}", strangerContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMember.StatusCode);

        Assert.Equal("Cơ sở A1", await world.WithDbAsync(db => db.Branches.Where(branch => branch.Id == world.BranchA1).Select(branch => branch.Name).SingleAsync(TestContext.Current.CancellationToken)));
        Assert.Equal("Cơ sở B1", await world.WithDbAsync(db => db.Branches.Where(branch => branch.Id == world.BranchB1).Select(branch => branch.Name).SingleAsync(TestContext.Current.CancellationToken)));
    }

    private static async Task<JsonDocument> PublicBranchesAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/v1/branches", TestContext.Current.CancellationToken);
        return await ReadAsync(response, HttpStatusCode.OK);
    }
}
