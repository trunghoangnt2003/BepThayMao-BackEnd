using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>Chống đơn trùng ở POST /orders: Idempotency-Key và cửa sổ trùng nội dung (T-02-29, UX-02).</summary>
public sealed class OrderDedupeTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private readonly Guid _branchA = Guid.CreateVersion7();
    private readonly Guid _branchB = Guid.CreateVersion7();
    private readonly Guid _dish = Guid.CreateVersion7();
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["RateLimiting:Public:PermitLimit"] = "1000",
        });
        await SeedAsync(_factory);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task SeedAsync(ApiFactory factory, TimeProvider? clock = null)
    {
        using IServiceScope scope = factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        DateTimeOffset now = (clock ?? TimeProvider.System).GetUtcNow();
        Guid owner = Guid.CreateVersion7();
        db.Owners.Add(Owner.Create(owner, "Owner dedupe test", now));
        db.Branches.Add(Branch.Create(_branchA, owner,
            new("Cơ sở A", "1 Đường Thử", "0901234567", 10.77, 106.70, new(0, 0), new(23, 59), 10, [], "0000000001", "970436", null, true), now));
        db.Branches.Add(Branch.Create(_branchB, owner,
            new("Cơ sở B", "2 Đường Thử", "0901234567", 10.80, 106.70, new(0, 0), new(23, 59), 10, [], "0000000002", "970436", null, true), now.AddSeconds(1)));
        db.Dishes.Add(Dish.Create(_dish, owner, new("Xôi xéo", "Đậu xanh", 25000, null, DishCategory.XoiMan, true, 0), now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid branchId, Guid dishId, string phone = "0901 234 567",
        string? idempotencyKey = null, int quantity = 1)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                customerName = "Khách thử",
                customerPhone = phone,
                deliveryAddress = "3 Đường Thử",
                branchId,
                nextDayAcknowledged = false,
                items = new[] { new { dishId, quantity } },
            }),
        };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return default;
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private async Task<int> CountOrdersAsync()
    {
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SameIdempotencyKeyReplaysFirstOrder()
    {
        HttpClient client = _factory.CreateClient();
        string key = Guid.NewGuid().ToString();

        using HttpResponseMessage first = await PlaceAsync(client, _branchA, _dish, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        JsonElement firstBody = await ReadJsonAsync(first);
        string code = firstBody.GetProperty("order").GetProperty("code").GetString()!;
        long amount = firstBody.GetProperty("payment").GetProperty("amount").GetInt64();

        using HttpResponseMessage second = await PlaceAsync(client, _branchA, _dish, idempotencyKey: key);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        JsonElement secondBody = await ReadJsonAsync(second);
        Assert.Equal(code, secondBody.GetProperty("order").GetProperty("code").GetString());
        Assert.Equal(amount, secondBody.GetProperty("payment").GetProperty("amount").GetInt64());
        Assert.NotNull(second.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookie) ? setCookie.FirstOrDefault() : null);

        Assert.Equal(1, await CountOrdersAsync());
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesOneOrder()
    {
        HttpClient client = _factory.CreateClient();
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PlaceAsync(client, _branchA, _dish, idempotencyKey: key)));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            string[] codes = await Task.WhenAll(responses.Select(async response =>
                (await ReadJsonAsync(response)).GetProperty("order").GetProperty("code").GetString()!));
            Assert.Single(codes.Distinct());
            Assert.Equal(1, await CountOrdersAsync());
        }
        finally
        {
            foreach (HttpResponseMessage response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task SameKeyDifferentPayloadReturns409()
    {
        HttpClient client = _factory.CreateClient();
        string key = Guid.NewGuid().ToString();

        using HttpResponseMessage first = await PlaceAsync(client, _branchA, _dish, idempotencyKey: key, quantity: 1);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage second = await PlaceAsync(client, _branchA, _dish, idempotencyKey: key, quantity: 2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountOrdersAsync());
    }

    [Fact]
    public async Task SameContentWithinWindowReplaysWithoutKey()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage first = await PlaceAsync(client, _branchA, _dish, phone: "0901 234 567");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        string code = (await ReadJsonAsync(first)).GetProperty("order").GetProperty("code").GetString()!;

        using HttpResponseMessage second = await PlaceAsync(client, _branchA, _dish, phone: "0901234567");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(code, (await ReadJsonAsync(second)).GetProperty("order").GetProperty("code").GetString());
        Assert.Equal(1, await CountOrdersAsync());
    }

    [Fact]
    public async Task OutsideWindowOrCancelledCreatesNewOrder()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["RateLimiting:Public:PermitLimit"] = "1000",
        }, clock);
        await using (factory)
        {
            await SeedAsync(factory, clock);
            HttpClient client = factory.CreateClient();

            using HttpResponseMessage first = await PlaceAsync(client, _branchA, _dish);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            string firstCode = (await ReadJsonAsync(first)).GetProperty("order").GetProperty("code").GetString()!;

            clock.Advance(TimeSpan.FromSeconds(121));
            using HttpResponseMessage afterWindow = await PlaceAsync(client, _branchA, _dish);
            Assert.Equal(HttpStatusCode.Created, afterWindow.StatusCode);
            string afterWindowCode = (await ReadJsonAsync(afterWindow)).GetProperty("order").GetProperty("code").GetString()!;
            Assert.NotEqual(firstCode, afterWindowCode);

            using HttpResponseMessage cancel = await client.PostAsJsonAsync($"/api/v1/orders/{afterWindowCode}/cancel",
                new { code = afterWindowCode, phone = "0901234567" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            using HttpResponseMessage afterCancel = await PlaceAsync(client, _branchA, _dish);
            Assert.Equal(HttpStatusCode.Created, afterCancel.StatusCode);
            string afterCancelCode = (await ReadJsonAsync(afterCancel)).GetProperty("order").GetProperty("code").GetString()!;
            Assert.NotEqual(afterWindowCode, afterCancelCode);

            using HttpResponseMessage otherBranch = await PlaceAsync(client, _branchB, _dish);
            Assert.Equal(HttpStatusCode.Created, otherBranch.StatusCode);
        }
    }

    [Fact]
    public async Task InvalidIdempotencyKeyReturns400()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await PlaceAsync(client, _branchA, _dish, idempotencyKey: "not-a-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
