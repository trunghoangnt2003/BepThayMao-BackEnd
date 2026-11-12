using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task DuplicateManagerIdentifierReturns409AndNoBranch()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        int branchesBefore = await world.WithDbAsync(db => db.Branches.CountAsync(TestContext.Current.CancellationToken));
        int usersBefore = await world.WithDbAsync(db => db.Users.CountAsync(TestContext.Current.CancellationToken));
        using HttpResponseMessage duplicate = await owner.PostAsJsonAsync("/api/v1/owner/branches",
            ValidBody(AuthWorld.IdentifierOf(AuthSubject.ManagerA1)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("manager.identifier", out _));
        Assert.Equal(branchesBefore, await world.WithDbAsync(db => db.Branches.CountAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(usersBefore, await world.WithDbAsync(db => db.Users.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task RoleAssignmentFailureLeavesNoBranchOrUser()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        await world.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM user_roles WHERE role_id IN (SELECT id FROM roles WHERE normalized_name = 'BRANCHMANAGER')", TestContext.Current.CancellationToken);
            return await db.Database.ExecuteSqlAsync($"DELETE FROM roles WHERE normalized_name = 'BRANCHMANAGER'", TestContext.Current.CancellationToken);
        });
        int branchesBefore = await world.WithDbAsync(db => db.Branches.CountAsync(TestContext.Current.CancellationToken));
        int usersBefore = await world.WithDbAsync(db => db.Users.CountAsync(TestContext.Current.CancellationToken));
        int rolesBefore = await world.WithDbAsync(db => db.UserRoles.CountAsync(TestContext.Current.CancellationToken));
        using HttpResponseMessage failed = await owner.PostAsJsonAsync("/api/v1/owner/branches", ValidBody("quanly-loi-vai-tro"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await failed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.Equal(branchesBefore, await world.WithDbAsync(db => db.Branches.CountAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(usersBefore, await world.WithDbAsync(db => db.Users.CountAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(rolesBefore, await world.WithDbAsync(db => db.UserRoles.CountAsync(TestContext.Current.CancellationToken)));
        Assert.False(await world.WithDbAsync(db => db.Users.AnyAsync(user => user.UserName == "quanly-loi-vai-tro", TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("branch", "closeTime", "\"05:00\"", "branch.openTime")]
    [InlineData("branch", "openTime", "\"6:00\"", "branch.openTime")]
    [InlineData("branch", "phone", "\"12345\"", "branch.phone")]
    [InlineData("branch", "lat", "91", "branch.lat")]
    [InlineData("branch", "abandonedThresholdMinutes", "0", "branch.abandonedThresholdMinutes")]
    [InlineData("branch", "zaloRecipientIds", "[\"a\",\"a\"]", "branch.zaloRecipientIds")]
    [InlineData("manager", "password", "\"123456789\"", "manager.password")]
    [InlineData("manager", "identifier", "\"co khoang trang\"", "manager.identifier")]
    public async Task InvalidBodyReturnsCamelCaseErrors(string section, string field, string json, string expectedKey)
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        JsonObject body = JsonSerializer.SerializeToNode(ValidBody("quanly-hop-le"))!.AsObject();
        body[section]![field] = JsonNode.Parse(json);
        using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await owner.PostAsync("/api/v1/owner/branches", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(expectedKey, out _), $"Thiếu khóa lỗi {expectedKey}.");
        Assert.Equal(0, await world.WithDbAsync(db => db.Users.CountAsync(user => user.UserName == "quanly-hop-le", TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("branch", "ownerId", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("manager", "role", "\"Owner\"")]
    public async Task UnknownMembersReturn400(string section, string field, string json)
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        JsonObject body = JsonSerializer.SerializeToNode(ValidBody("quanly-la"))!.AsObject();
        body[section]![field] = JsonNode.Parse(json);
        using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await owner.PostAsync("/api/v1/owner/branches", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(await world.WithDbAsync(db => db.Users.AnyAsync(user => user.UserName == "quanly-la", TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task RoleAndAnonymousAreRejected()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient manager = await world.ClientForAsync(AuthSubject.ManagerA1);
        using HttpClient anonymous = await world.ClientForAsync(AuthSubject.Anonymous);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/v1/owner/branches", ValidBody("quanly-sai-quyen"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/v1/owner/branches", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/v1/owner/branches/{world.BranchA1}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/owner/branches", ValidBody("quanly-an-danh"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/owner/branches", TestContext.Current.CancellationToken)).StatusCode);
        Assert.False(await world.WithDbAsync(db => db.Users.AnyAsync(user => user.UserName == "quanly-sai-quyen" || user.UserName == "quanly-an-danh", TestContext.Current.CancellationToken)));
    }
}
