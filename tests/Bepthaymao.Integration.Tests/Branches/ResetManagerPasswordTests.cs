using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Integration.Tests.Branches;

public sealed class ResetManagerPasswordTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>
{
    private const string _newPassword = "mat-khau-moi-12";
    private static string ResetPath(Guid branchId) => $"/api/v1/owner/branches/{branchId}/manager/reset-password";

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string identifier, string password) =>
        await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier, password }, TestContext.Current.CancellationToken);

    private static string Cookie(HttpResponseMessage response) => Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResetChangesPasswordAndRevokesSessions()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpClient managerClient = world.Factory.CreateClient();
        string managerName = AuthWorld.IdentifierOf(AuthSubject.ManagerA1);
        using HttpResponseMessage firstLogin = await LoginAsync(managerClient, managerName, AuthWorld.Password);
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        string oldCookie = Cookie(firstLogin);
        string stampBefore = await world.WithDbAsync(db => db.Users.Where(user => user.UserName == managerName).Select(user => user.SecurityStamp!).SingleAsync(TestContext.Current.CancellationToken));
        Guid managerId = await world.WithDbAsync(db => db.Users.Where(user => user.UserName == managerName).Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken));
        Assert.All(await world.WithDbAsync(db => db.RefreshTokens.Where(token => token.UserId == managerId).ToArrayAsync(TestContext.Current.CancellationToken)),
            token => Assert.Null(token.RevokedAt));

        using HttpResponseMessage reset = await owner.PostAsJsonAsync(ResetPath(world.BranchA1), new { newPassword = _newPassword }, TestContext.Current.CancellationToken);
        Assert.True(reset.StatusCode == HttpStatusCode.NoContent, $"Mong 204 nhưng nhận {(int)reset.StatusCode}: {await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");

        using HttpResponseMessage oldPassword = await LoginAsync(managerClient, managerName, AuthWorld.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using HttpResponseMessage newPassword = await LoginAsync(managerClient, managerName, _newPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        using HttpResponseMessage staleRefresh = await RefreshAsync(managerClient, oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);

        Assert.NotEqual(stampBefore, await world.WithDbAsync(db => db.Users.Where(user => user.Id == managerId).Select(user => user.SecurityStamp!).SingleAsync(TestContext.Current.CancellationToken)));
        var tokens = await world.WithDbAsync(db => db.RefreshTokens.Where(token => token.UserId == managerId).OrderBy(token => token.CreatedAt).ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, tokens.Length);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.Null(tokens[1].RevokedAt);
        Guid ownerId = await world.WithDbAsync(db => db.Users.Where(user => user.UserName == AuthWorld.IdentifierOf(AuthSubject.Owner1)).Select(user => user.Id).SingleAsync(TestContext.Current.CancellationToken));
        Assert.All(await world.WithDbAsync(db => db.RefreshTokens.Where(token => token.UserId == ownerId).ToArrayAsync(TestContext.Current.CancellationToken)),
            token => Assert.Null(token.RevokedAt));
    }

    [Fact]
    public async Task Rejections()
    {
        await using AuthWorld world = await AuthWorld.CreateAsync(infrastructure);
        using HttpClient owner = await world.ClientForAsync(AuthSubject.Owner1);
        using HttpClient anonymous = world.Factory.CreateClient();

        using HttpResponseMessage tooShort = await owner.PostAsJsonAsync(ResetPath(world.BranchA1), new { newPassword = "123456789" }, TestContext.Current.CancellationToken);
        string shortText = await tooShort.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(tooShort.StatusCode == HttpStatusCode.BadRequest, $"Mong 400 nhưng nhận {(int)tooShort.StatusCode}: {shortText}");
        using JsonDocument problem = JsonDocument.Parse(shortText);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("newPassword", out _), "Thiếu khóa lỗi newPassword.");

        Guid managerless = Guid.CreateVersion7();
        await world.WithDbAsync(async db =>
        {
            db.Branches.Add(Branch.Create(managerless, world.Owner1Id,
                new("Cơ sở chưa có quản lý", "2 Đường Thử", "0901234567", 10.77, 106.7, new(6, 0), new(21, 30), 10, [], null, null, null, true), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
        using HttpResponseMessage noManager = await owner.PostAsJsonAsync(ResetPath(managerless), new { newPassword = _newPassword }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, noManager.StatusCode);

        using HttpResponseMessage foreign = await owner.PostAsJsonAsync(ResetPath(world.BranchB1), new { newPassword = _newPassword }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        foreach (AuthSubject manager in new[] { AuthSubject.ManagerA1, AuthSubject.ManagerB1 })
        {
            using HttpResponseMessage stillWorks = await LoginAsync(anonymous, AuthWorld.IdentifierOf(manager), AuthWorld.Password);
            Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
        }
    }
}
