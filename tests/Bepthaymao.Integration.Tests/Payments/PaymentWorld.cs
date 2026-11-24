using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Application.Orders;
using Bepthaymao.Application.Payments;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bepthaymao.Integration.Tests.Payments;

/// <summary>
/// Dữ liệu dùng chung cho test thanh toán: hai Owner, các cơ sở có tài khoản nhận tiền riêng và trùng nhau để thử phạm vi sổ thanh toán.
/// </summary>
internal sealed class PaymentWorld : IAsyncDisposable
{
    public const string Key = "test-only-sepay-webhook-secret-1234";
    public const string Password = "test-payment-password-123";
    public const string BankCode = "970415";
    public const string Phone = "0900000001";

    // Tài khoản 123456 được cả Owner A (cơ sở A) và Owner Z (cơ sở Z) khai báo: không đoán được chủ.
    public const string SharedAccount = "123456";
    // Chỉ cơ sở B (Owner A) dùng.
    public const string UniqueAccount = "555555";
    // Hai cơ sở C, D cùng Owner A dùng chung: chỉ biết Owner, không biết cơ sở.
    public const string SameOwnerAccount = "666666";

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public Guid OwnerA { get; } = Guid.NewGuid();
    public Guid OwnerZ { get; } = Guid.NewGuid();
    public Guid BranchA { get; } = Guid.NewGuid();
    public Guid BranchB { get; } = Guid.NewGuid();
    public Guid BranchC { get; } = Guid.NewGuid();
    public Guid BranchD { get; } = Guid.NewGuid();
    public Guid BranchZ { get; } = Guid.NewGuid();
    public Guid DishId { get; } = Guid.NewGuid();
    public ApiFactory Factory { get; private set; } = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<PaymentWorld> CreateAsync(InfrastructureFixture infrastructure)
    {
        PaymentWorld world = new();
        world.Factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = await infrastructure.CreateDatabaseAsync(),
            ["ConnectionStrings:RabbitMq"] = infrastructure.RabbitConnectionString,
            ["SePay:WebhookKey"] = Key
        }, world.Clock);
        using IServiceScope scope = world.Factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Token);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "owner-a", Password, UserRole.Owner, world.OwnerA);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "owner-z", Password, UserRole.Owner, world.OwnerZ);
        db.Branches.AddRange(world.NewBranch(world.BranchA, world.OwnerA, "Cơ sở A", SharedAccount),
            world.NewBranch(world.BranchB, world.OwnerA, "Cơ sở B", UniqueAccount),
            world.NewBranch(world.BranchC, world.OwnerA, "Cơ sở C", SameOwnerAccount),
            world.NewBranch(world.BranchD, world.OwnerA, "Cơ sở D", SameOwnerAccount),
            world.NewBranch(world.BranchZ, world.OwnerZ, "Cơ sở Z", SharedAccount));
        db.Dishes.Add(Dish.Create(world.DishId, world.OwnerA, new("Xôi", null, 40000, null, DishCategory.XoiMan, false, 0), world.Clock.GetUtcNow()));
        await db.SaveChangesAsync(Token);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "manager-a", Password, UserRole.BranchManager, branchId: world.BranchA);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "manager-b", Password, UserRole.BranchManager, branchId: world.BranchB);
        return world;
    }

    private Branch NewBranch(Guid id, Guid owner, string name, string account) => Branch.Create(id, owner,
        new(name, "Test street", "0900000000", 10.7, 106.7, new(0, 0), new(23, 59), 10, [], account, BankCode, null, true), Clock.GetUtcNow());

    public IServiceScope OpenScope() => Factory.Services.CreateSystemScope();

    public async Task<Order> PlaceAsync(Guid? branch = null, int minutesAgo = 0, string account = SharedAccount)
    {
        Order order = Order.Place(Guid.NewGuid(), OrderCodes.Next(), branch ?? BranchA, new("Test", Phone, "Test", null, null, null, null),
            [OrderItem.Create(Guid.NewGuid(), DishId, "Xôi", 40000, 1)], false, new(2026, 11, 23), Clock.GetUtcNow().AddMinutes(-minutesAgo));
        order.CapturePaymentDestination(BankCode, account);
        using IServiceScope scope = OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Token);
        return order;
    }

    /// <summary>Đơn hết hạn như Worker làm (03-03), nhưng gọi thẳng miền để 03-02 không phụ thuộc Worker.</summary>
    public async Task ExpireAsync(Order order)
    {
        using IServiceScope scope = OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order tracked = await db.Orders.SingleAsync(value => value.Id == order.Id, Token);
        Assert.True(tracked.Expire(Clock.GetUtcNow()));
        await db.SaveChangesAsync(Token);
    }

    public SePayTransfer Transfer(Order order, long id = 1, long amount = 40000, string account = SharedAccount) => new(id, "VietinBank",
        Clock.GetUtcNow().AddHours(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), account, order.Code, "in", amount);

    public SePayTransfer Unmatched(long id, string account, string content = "chuyen tien") => new(id, "VietinBank",
        Clock.GetUtcNow().AddHours(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), account, content, "in", 40000);

    public async Task<HttpResponseMessage> SendAsync(SePayTransfer transfer, string key = Key)
    {
        using HttpClient client = Factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/webhooks/sepay") { Content = JsonContent.Create(transfer) };
        request.Headers.Authorization = new("Apikey", key);
        return await client.SendAsync(request, Token);
    }

    public async Task<HttpClient> LoginAsync(string identifier)
    {
        HttpClient client = Factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier, password = Password }, Token);
        response.EnsureSuccessStatusCode();
        JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        client.DefaultRequestHeaders.Authorization = new("Bearer", result.GetProperty("accessToken").GetString());
        return client;
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
