using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Application.Orders;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Orders;

/// <summary>Thực đơn chung, chọn cơ sở sau, đặt đơn có QR, tra cứu/hủy, danh sách theo phạm vi (Phase 2, FR-003a, FR-005a).</summary>
public sealed class MenuOrderFlowTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const string _password = "mat-khau-kiem-thu-12";
    private ApiFactory _factory = null!;
    private readonly Guid _owner1 = Guid.CreateVersion7();
    private readonly Guid _owner2 = Guid.CreateVersion7();
    private readonly Guid _open = Guid.CreateVersion7();
    private readonly Guid _closed = Guid.CreateVersion7();
    private readonly Guid _noBank = Guid.CreateVersion7();
    private readonly Guid _otherOwner = Guid.CreateVersion7();
    private readonly Guid _available = Guid.CreateVersion7();
    private readonly Guid _soldOutAtOpen = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        _factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["RateLimiting:Public:PermitLimit"] = "1000",
        });
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "owner-one", _password, UserRole.Owner, _owner1);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "owner-two", _password, UserRole.Owner, _owner2);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeOnly local = TimeOnly.FromDateTime(OpeningHours.ToLocal(now).DateTime);
        // Cơ sở "đóng" mở một giờ cách thời điểm chạy ít nhất hai giờ, không qua nửa đêm.
        TimeOnly closedOpen = local.Hour < 12 ? new(local.Hour + 3, 0) : new(local.Hour - 3, 0);
        db.Branches.AddRange(
            Branch.Create(_open, _owner1, Details("Mở cửa", 10.77, 106.70, new(0, 0), new(23, 59), bank: true), now),
            Branch.Create(_closed, _owner1, Details("Đóng cửa", 10.80, 106.70, closedOpen, closedOpen.AddHours(1), bank: true), now.AddSeconds(1)),
            Branch.Create(_noBank, _owner1, Details("Thiếu ngân hàng", 11.50, 107.00, new(0, 0), new(23, 59), bank: false), now.AddSeconds(2)),
            Branch.Create(_otherOwner, _owner2, Details("Chủ khác", 10.77, 106.71, new(0, 0), new(23, 59), bank: true), now.AddSeconds(3)));
        db.Dishes.AddRange(
            Dish.Create(_available, _owner1, new("Xôi xéo", "Đậu xanh", 25000, null, DishCategory.XoiMan, true, 0), now),
            Dish.Create(_soldOutAtOpen, _owner1, new("Xôi gấc", null, 30000, null, DishCategory.XoiNgot, false, 1), now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.BranchDishes.Add(BranchDish.Create(_open, _soldOutAtOpen, false, now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "manager-open", _password, UserRole.BranchManager, null, _open);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static BranchDetails Details(string name, double lat, double lng, TimeOnly open, TimeOnly close, bool bank) =>
        new(name, "1 Đường Thử", "0901234567", lat, lng, open, close, 10, [], bank ? "0000000001" : null, bank ? "970436" : null, null, true);

    [Fact]
    public async Task PublicMenuListsSharedDishesWithBranchesWhereSoldOut()
    {
        JsonElement menu = await GetJsonAsync(_factory.CreateClient(), "/api/v1/menu");
        JsonElement soldOut = menu.EnumerateArray().Single(dish => dish.GetProperty("id").GetGuid() == _soldOutAtOpen);
        Guid[] unavailable = [.. soldOut.GetProperty("unavailableBranchIds").EnumerateArray().Select(id => id.GetGuid())];
        Assert.Contains(_open, unavailable);
        Assert.Contains(_otherOwner, unavailable); // món của Owner khác không bán ở cơ sở của Owner 2
        Assert.DoesNotContain(_closed, unavailable);
        Assert.Equal("XoiNgot", soldOut.GetProperty("category").GetString());
    }

    [Fact]
    public async Task BranchOptionsSuggestNearestAndFlagMissingDishesAndPayment()
    {
        using HttpResponseMessage response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/checkout/branch-options",
            new { deliveryLat = 10.7701, deliveryLng = 106.7001, dishIds = new[] { _available, _soldOutAtOpen } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement[] options = [.. (await ReadJsonAsync(response)).EnumerateArray()];
        Assert.Equal(_open, options[0].GetProperty("branchId").GetGuid());
        Assert.True(options[0].GetProperty("isSuggested").GetBoolean());
        Assert.Contains(_soldOutAtOpen, options[0].GetProperty("unavailableDishIds").EnumerateArray().Select(id => id.GetGuid()));
        JsonElement closed = options.Single(option => option.GetProperty("branchId").GetGuid() == _closed);
        Assert.False(closed.GetProperty("isOpenNow").GetBoolean());
        Assert.True(closed.GetProperty("nextDayWarning").GetBoolean());
        Assert.False(options.Single(option => option.GetProperty("branchId").GetGuid() == _noBank).GetProperty("acceptsOrders").GetBoolean());
        Assert.Equal(1, options.Count(option => option.GetProperty("isSuggested").GetBoolean()));
    }

    [Fact]
    public async Task OrderIsCreatedWithQrAndConflictsAreExplained()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage created = await PlaceAsync(client, _open, _available, acknowledged: false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement body = await ReadJsonAsync(created);
        Assert.StartsWith("BTM", body.GetProperty("order").GetProperty("code").GetString(), StringComparison.Ordinal);
        Assert.Equal(50000, body.GetProperty("payment").GetProperty("amount").GetInt64());
        Assert.StartsWith("data:image/svg+xml;base64,", body.GetProperty("payment").GetProperty("qrUrl").GetString(), StringComparison.Ordinal);
        Assert.False(body.GetProperty("order").GetProperty("isNextDay").GetBoolean());

        using HttpResponseMessage soldOut = await PlaceAsync(client, _open, _soldOutAtOpen, acknowledged: false);
        Assert.Equal(HttpStatusCode.Conflict, soldOut.StatusCode);
        Assert.Contains(_soldOutAtOpen, (await ReadJsonAsync(soldOut)).GetProperty("unavailableDishIds").EnumerateArray().Select(id => id.GetGuid()));

        using HttpResponseMessage noBank = await PlaceAsync(client, _noBank, _available, acknowledged: false);
        Assert.Equal(HttpStatusCode.Conflict, noBank.StatusCode);

        using HttpResponseMessage unacknowledged = await PlaceAsync(client, _closed, _available, acknowledged: false);
        Assert.Equal(HttpStatusCode.Conflict, unacknowledged.StatusCode);
        Assert.True((await ReadJsonAsync(unacknowledged)).GetProperty("errors").TryGetProperty("nextDayAcknowledged", out _));

        using HttpResponseMessage nextDay = await PlaceAsync(client, _closed, _available, acknowledged: true);
        Assert.Equal(HttpStatusCode.Created, nextDay.StatusCode);
        Assert.True((await ReadJsonAsync(nextDay)).GetProperty("order").GetProperty("isNextDay").GetBoolean());
    }

    [Fact]
    public async Task TrackingNeedsMatchingPhoneAndCancelWorksOnce()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage created = await PlaceAsync(client, _open, _available, acknowledged: false);
        string code = (await ReadJsonAsync(created)).GetProperty("order").GetProperty("code").GetString()!;

        using HttpResponseMessage wrongPhone = await client.GetAsync($"/api/v1/orders/{code}?phone=0999999999", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);
        JsonElement tracking = await GetJsonAsync(client, $"/api/v1/orders/{code.ToLowerInvariant()}?phone=0901%20234%20567");
        Assert.Equal("PendingPayment", tracking.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, tracking.GetProperty("payment").ValueKind);

        using HttpResponseMessage cancelled = await client.PostAsJsonAsync($"/api/v1/orders/{code}/cancel", new { code, phone = "+84901234567" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        JsonElement after = await GetJsonAsync(client, $"/api/v1/orders/{code}?phone=0901234567");
        Assert.Equal("Cancelled", after.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("payment").ValueKind);
        using HttpResponseMessage again = await client.PostAsJsonAsync($"/api/v1/orders/{code}/cancel", new { code, phone = "0901234567" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task OrderListsFollowOwnerAndManagerScope()
    {
        HttpClient guest = _factory.CreateClient();
        (await PlaceAsync(guest, _open, _available, acknowledged: false)).Dispose();
        (await PlaceAsync(guest, _closed, _available, acknowledged: true)).Dispose();
        (await PlaceOtherOwnerAsync(guest)).Dispose();

        Guid[] ownerOne = await BranchIdsAsync(await LoginAsync("owner-one"), "/api/v1/owner/orders");
        Assert.Contains(_open, ownerOne);
        Assert.Contains(_closed, ownerOne);
        Assert.DoesNotContain(_otherOwner, ownerOne);

        Guid[] manager = await BranchIdsAsync(await LoginAsync("manager-open"), "/api/v1/manager/orders");
        Assert.NotEmpty(manager);
        Assert.All(manager, branch => Assert.Equal(_open, branch));

        HttpClient owner = await LoginAsync("owner-one");
        Assert.All(await BranchIdsAsync(owner, $"/api/v1/owner/orders?branchId={_closed}"), branch => Assert.Equal(_closed, branch));
        using HttpResponseMessage foreign = await owner.GetAsync($"/api/v1/owner/orders?branchId={_otherOwner}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        Guid[] ownerTwo = await BranchIdsAsync(await LoginAsync("owner-two"), "/api/v1/owner/orders");
        Assert.All(ownerTwo, branch => Assert.Equal(_otherOwner, branch));
    }

    [Fact]
    public async Task OverviewCountsOnlyBranchesInScope()
    {
        HttpClient guest = _factory.CreateClient();
        (await PlaceAsync(guest, _open, _available, acknowledged: false)).Dispose();
        (await PlaceOtherOwnerAsync(guest)).Dispose();

        HttpClient owner = await LoginAsync("owner-one");
        JsonElement overview = await GetJsonAsync(owner, "/api/v1/owner/overview");
        Guid[] branches = [.. overview.GetProperty("branches").EnumerateArray().Select(branch => branch.GetProperty("branchId").GetGuid())];
        Assert.Equal([_open, _closed, _noBank], branches);
        Assert.True(overview.GetProperty("ordersToday").GetInt32() >= 1);
        Assert.Equal(16, overview.GetProperty("ordersByHour").GetArrayLength());
        Assert.Equal(7, overview.GetProperty("days").GetArrayLength());
        Assert.Contains(overview.GetProperty("soldOut").EnumerateArray(), item => item.GetProperty("dishName").GetString() == "Xôi gấc");
        Assert.Contains(overview.GetProperty("topDishes").EnumerateArray(), item => item.GetProperty("dishName").GetString() == "Xôi xéo");
        Assert.DoesNotContain(overview.GetProperty("topDishes").EnumerateArray(), item => item.GetProperty("dishName").GetString() == "Xôi chủ khác");

        JsonElement closedOnly = await GetJsonAsync(owner, $"/api/v1/owner/overview?branchId={_closed}");
        Assert.Equal(_closed, closedOnly.GetProperty("branches").EnumerateArray().Single().GetProperty("branchId").GetGuid());
        using HttpResponseMessage foreign = await owner.GetAsync($"/api/v1/owner/overview?branchId={_otherOwner}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        JsonElement manager = await GetJsonAsync(await LoginAsync("manager-open"), "/api/v1/manager/overview");
        Assert.Equal(_open, manager.GetProperty("branches").EnumerateArray().Single().GetProperty("branchId").GetGuid());
    }

    [Fact]
    public async Task ManagerTogglesOnlyOwnBranchAndOwnerSeesItInMenu()
    {
        HttpClient manager = await LoginAsync("manager-open");
        using HttpResponseMessage toggled = await manager.PutAsJsonAsync($"/api/v1/manager/dishes/{_available}/availability",
            new { isAvailable = false }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, toggled.StatusCode);
        JsonElement dish = (await GetJsonAsync(await LoginAsync("owner-one"), "/api/v1/owner/dishes")).EnumerateArray()
            .Single(value => value.GetProperty("id").GetGuid() == _available);
        Assert.Equal(1, dish.GetProperty("unavailableBranchCount").GetInt32());
        using HttpResponseMessage restored = await manager.PutAsJsonAsync($"/api/v1/manager/dishes/{_available}/availability",
            new { isAvailable = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public void VietQrChecksumMatchesCcittFalseCheckValue() =>
        Assert.Equal(0x29B1, Bepthaymao.Infrastructure.Payments.VietQrService.Crc16("123456789"));

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid branchId, Guid dishId, bool acknowledged) =>
        await client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerName = "Khách thử",
            customerPhone = "0901 234 567",
            deliveryAddress = "2 Đường Thử",
            branchId,
            nextDayAcknowledged = acknowledged,
            items = new[] { new { dishId, quantity = 2 } },
        }, TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> PlaceOtherOwnerAsync(HttpClient client)
    {
        Guid dish;
        using (IServiceScope scope = _factory.Services.CreateSystemScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Dish created = Dish.Create(Guid.CreateVersion7(), _owner2, new("Xôi chủ khác", null, 10000, null, DishCategory.Chay, false, 0), DateTimeOffset.UtcNow);
            db.Dishes.Add(created);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            dish = created.Id;
        }
        return await PlaceAsync(client, _otherOwner, dish, acknowledged: false);
    }

    private async Task<HttpClient> LoginAsync(string identifier)
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier, password = _password }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadJsonAsync(response)).GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<Guid[]> BranchIdsAsync(HttpClient client, string path) =>
        [.. (await GetJsonAsync(client, path)).EnumerateArray().Select(order => order.GetProperty("branchId").GetGuid())];

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }
}
