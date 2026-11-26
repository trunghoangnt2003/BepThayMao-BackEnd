using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Notifications;

[Collection(SharedInfrastructure.Name)]
public sealed class NotificationEndpointTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private async Task SeedAsync(Guid branchId, NotificationChannel channel, string orderCode, int minutesAgo = 0)
    {
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        DateTimeOffset at = _world.Clock.GetUtcNow().AddMinutes(-minutesAgo);
        db.Notifications.Add(new()
        {
            Id = Guid.CreateVersion7(),
            Type = NotificationType.OrderNew,
            Channel = channel,
            Status = NotificationStatus.Sent,
            Attempts = 1,
            BranchId = branchId,
            OrderId = Guid.NewGuid(),
            OrderCode = orderCode,
            LastError = "chi tiết nội bộ không được lộ",
            CreatedAt = at,
            SentAt = at
        });
        await db.SaveChangesAsync(Token);
    }

    private static async Task<List<string?>> CodesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Codes(await response.Content.ReadFromJsonAsync<JsonElement>(Token));
    }

    private static List<string?> Codes(JsonElement body) =>
        [.. body.EnumerateArray().Select(item => item.GetProperty("orderCode").GetString())];

    [Fact]
    public async Task ManagerSeesOnlyOwnBranchInAppNotificationsNewestFirstWithoutInternalFields()
    {
        await SeedAsync(_world.BranchA, NotificationChannel.InApp, "A-OLD", minutesAgo: 5);
        await SeedAsync(_world.BranchA, NotificationChannel.InApp, "A-NEW");
        await SeedAsync(_world.BranchA, NotificationChannel.Telegram, "A-TELEGRAM");
        await SeedAsync(_world.BranchB, NotificationChannel.InApp, "B-OTHER");
        using HttpClient manager = await _world.LoginAsync("manager-a");

        using HttpResponseMessage response = await manager.GetAsync("/api/v1/manager/notifications", Token);
        string raw = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(["A-NEW", "A-OLD"], Codes(JsonSerializer.Deserialize<JsonElement>(raw)));
        Assert.DoesNotContain("lastError", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chi tiết nội bộ", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerSeesAllOwnedBranchesAndNothingFromAnotherOwner()
    {
        await SeedAsync(_world.BranchA, NotificationChannel.InApp, "A-1");
        await SeedAsync(_world.BranchB, NotificationChannel.InApp, "B-1");
        await SeedAsync(_world.BranchZ, NotificationChannel.InApp, "Z-1");
        using HttpClient owner = await _world.LoginAsync("owner-a");

        List<string?> codes = await CodesAsync(await owner.GetAsync("/api/v1/owner/notifications", Token));

        Assert.Equal(2, codes.Count);
        Assert.Contains("A-1", codes);
        Assert.Contains("B-1", codes);
    }

    [Fact]
    public async Task OwnerCanFilterByOwnBranchButNotByAnotherOwnersBranch()
    {
        await SeedAsync(_world.BranchA, NotificationChannel.InApp, "A-1");
        await SeedAsync(_world.BranchB, NotificationChannel.InApp, "B-1");
        await SeedAsync(_world.BranchZ, NotificationChannel.InApp, "Z-1");
        using HttpClient owner = await _world.LoginAsync("owner-a");

        Assert.Equal(["B-1"], await CodesAsync(await owner.GetAsync($"/api/v1/owner/notifications?branchId={_world.BranchB}", Token)));
        using HttpResponseMessage foreign = await owner.GetAsync($"/api/v1/owner/notifications?branchId={_world.BranchZ}", Token);
        Assert.True(foreign.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"Nhận {foreign.StatusCode}");
        using HttpResponseMessage unknown = await owner.GetAsync($"/api/v1/owner/notifications?branchId={Guid.NewGuid()}", Token);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task PagesHaveTwentyRowsAndOutOfRangePageIsEmpty()
    {
        for (int index = 0; index < 21; index++)
            await SeedAsync(_world.BranchA, NotificationChannel.InApp, $"A-{index:D2}", minutesAgo: 30 - index);
        using HttpClient manager = await _world.LoginAsync("manager-a");

        Assert.Equal(20, (await CodesAsync(await manager.GetAsync("/api/v1/manager/notifications", Token))).Count);
        Assert.Single(await CodesAsync(await manager.GetAsync("/api/v1/manager/notifications?page=2", Token)));
        Assert.Empty(await CodesAsync(await manager.GetAsync("/api/v1/manager/notifications?page=3", Token)));
        Assert.Equal(20, (await CodesAsync(await manager.GetAsync("/api/v1/manager/notifications?page=-4", Token))).Count);
    }

    [Fact]
    public async Task RolesAndAnonymousAreSeparated()
    {
        using HttpClient anonymous = _world.Factory.CreateClient();
        using HttpClient manager = await _world.LoginAsync("manager-a");
        using HttpClient owner = await _world.LoginAsync("owner-a");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/manager/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/owner/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/v1/owner/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/manager/notifications", Token)).StatusCode);
    }
}
