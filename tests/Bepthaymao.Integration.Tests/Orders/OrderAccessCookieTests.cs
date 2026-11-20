using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bepthaymao.Api.Security;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Owners;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bepthaymao.Integration.Tests.Orders;

/// <summary>Cookie HttpOnly btm_don gắn đơn: cấp khi tạo đơn/tra cứu, mở và hủy đơn không cần ?phone= trên URL (P2-NOJS, SEC-01).</summary>
public sealed partial class OrderAccessCookieTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const string _password = "mat-khau-kiem-thu-12";
    private readonly Guid _branch = Guid.CreateVersion7();
    private readonly Guid _dish = Guid.CreateVersion7();
    private ApiFactory _factory = null!;

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenShape();

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
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid owner = Guid.CreateVersion7();
        db.Owners.Add(Owner.Create(owner, "Owner cookie test", now));
        db.Branches.Add(Branch.Create(_branch, owner,
            new("Cơ sở cookie", "1 Đường Thử", "0901234567", 10.77, 106.70, new(0, 0), new(23, 59), 10, [], "0000000001", "970436", null, true), now));
        db.Dishes.Add(Dish.Create(_dish, owner, new("Xôi xéo", "Đậu xanh", 25000, null, DishCategory.XoiMan, true, 0), now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(HttpResponseMessage Response, JsonElement Body, string? Cookie)> PlaceAsync(HttpClient client, string phone = "0901 234 567")
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerName = "Khách thử",
            customerPhone = phone,
            deliveryAddress = "2 Đường Thử",
            branchId = _branch,
            nextDayAcknowledged = false,
            items = new[] { new { dishId = _dish, quantity = 1 } },
        }, TestContext.Current.CancellationToken);
        JsonElement body = await ReadJsonAsync(response);
        string? cookie = FirstSetCookie(response);
        return (response, body, cookie);
    }

    private static string? FirstSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    private static string CookieValue(string setCookieHeader) => setCookieHeader.Split(';')[0]["btm_don=".Length..];

    private static HttpRequestMessage WithCookie(HttpMethod method, string path, string? cookieValue, object? body = null)
    {
        HttpRequestMessage request = new(method, path);
        if (cookieValue is not null) request.Headers.Add("Cookie", $"btm_don={cookieValue}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return default;
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task CreateOrderSetsHttpOnlyLaxCookieWithoutPersonalData()
    {
        HttpClient client = _factory.CreateClient();
        (HttpResponseMessage response, JsonElement body, string? setCookie) = await PlaceAsync(client, "0901 234 567");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(setCookie);
        string header = setCookie!;
        Assert.StartsWith("btm_don=", header, StringComparison.Ordinal);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", header, StringComparison.OrdinalIgnoreCase);
        string value = CookieValue(header);
        Assert.Matches(TokenShape(), value);
        string code = body.GetProperty("order").GetProperty("code").GetString()!;
        Assert.DoesNotContain("0901234567", value, StringComparison.Ordinal);
        Assert.DoesNotContain("0901 234 567", value, StringComparison.Ordinal);
        Assert.DoesNotContain(code, value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CookieAloneOpensOwnOrder()
    {
        HttpClient client = _factory.CreateClient();
        (HttpResponseMessage created, JsonElement body, string? setCookie) = await PlaceAsync(client);
        string code = body.GetProperty("order").GetProperty("code").GetString()!;
        string cookie = CookieValue(setCookie!);

        using HttpResponseMessage response = await client.SendAsync(
            WithCookie(HttpMethod.Get, $"/api/v1/orders/{code}", cookie), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement tracking = await ReadJsonAsync(response);
        Assert.Equal(code, tracking.GetProperty("order").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Object, tracking.GetProperty("payment").ValueKind);
    }

    [Fact]
    public async Task CookieForAnotherOrderOrForgedTokenReturns404()
    {
        HttpClient client = _factory.CreateClient();
        (_, JsonElement bodyA, string? setCookieA) = await PlaceAsync(client, "0901 111 111");
        (_, JsonElement bodyB, _) = await PlaceAsync(client, "0902 222 222");
        string codeA = bodyA.GetProperty("order").GetProperty("code").GetString()!;
        string codeB = bodyB.GetProperty("order").GetProperty("code").GetString()!;
        string cookieA = CookieValue(setCookieA!);

        using HttpResponseMessage wrongOrder = await client.SendAsync(
            WithCookie(HttpMethod.Get, $"/api/v1/orders/{codeB}", cookieA), TestContext.Current.CancellationToken);
        string referenceBody = await wrongOrder.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, wrongOrder.StatusCode);

        string forged = new('a', 43);
        using HttpResponseMessage forgedToken = await client.SendAsync(
            WithCookie(HttpMethod.Get, $"/api/v1/orders/{codeA}", forged), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, forgedToken.StatusCode);

        using HttpResponseMessage neither = await client.GetAsync($"/api/v1/orders/{codeA}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, neither.StatusCode);

        string forgedBody = await forgedToken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string neitherBody = await neither.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using HttpResponseMessage wrongPhone = await client.GetAsync($"/api/v1/orders/{codeA}?phone=0999999999", TestContext.Current.CancellationToken);
        string wrongPhoneBody = await wrongPhone.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);
        Assert.Equal(wrongPhoneBody, referenceBody);
        Assert.Equal(wrongPhoneBody, forgedBody);
        Assert.Equal(wrongPhoneBody, neitherBody);
    }

    [Fact]
    public async Task ExpiredTokenReturns404()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["RateLimiting:Public:PermitLimit"] = "1000",
        }, clock);
        await using (factory)
        {
            using (IServiceScope scope = factory.Services.CreateSystemScope())
            {
                AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
                DateTimeOffset now = clock.GetUtcNow();
                Guid owner = Guid.CreateVersion7();
                db.Owners.Add(Owner.Create(owner, "Owner cookie test", now));
                db.Branches.Add(Branch.Create(_branch, owner,
                    new("Cơ sở cookie", "1 Đường Thử", "0901234567", 10.77, 106.70, new(0, 0), new(23, 59), 10, [], "0000000001", "970436", null, true), now));
                db.Dishes.Add(Dish.Create(_dish, owner, new("Xôi xéo", "Đậu xanh", 25000, null, DishCategory.XoiMan, true, 0), now));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
            HttpClient client = factory.CreateClient();
            (_, JsonElement body, string? setCookie) = await PlaceAsync(client);
            string code = body.GetProperty("order").GetProperty("code").GetString()!;
            string cookie = CookieValue(setCookie!);
            clock.Advance(TimeSpan.FromHours(25));
            using HttpResponseMessage response = await client.SendAsync(
                WithCookie(HttpMethod.Get, $"/api/v1/orders/{code}", cookie), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task GrantTableStoresOnlyHash()
    {
        HttpClient client = _factory.CreateClient();
        (_, JsonElement body, string? setCookie) = await PlaceAsync(client);
        string code = body.GetProperty("order").GetProperty("code").GetString()!;
        string token = CookieValue(setCookie!);

        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Guid orderId = await db.Orders.IgnoreQueryFilters().Where(order => order.Code == code).Select(order => order.Id).SingleAsync(TestContext.Current.CancellationToken);
        var grant = await db.Database.SqlQuery<GrantRow>(
            $"SELECT order_id AS \"OrderId\", token_hash AS \"TokenHash\", expires_at AS \"ExpiresAt\" FROM order_access_grants WHERE order_id = {orderId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(orderId, grant.OrderId);
        Assert.Equal(64, grant.TokenHash.Length);
        Assert.NotEqual(token, grant.TokenHash);
        Assert.True(grant.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
    }

    private sealed record GrantRow
    {
        public Guid OrderId { get; init; }
        public string TokenHash { get; init; } = "";
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
