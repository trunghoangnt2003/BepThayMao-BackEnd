using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bepthaymao.Api.Security;

namespace Bepthaymao.Integration.Tests.Infrastructure;

internal sealed record TestDataScope(bool IsSystem, Guid? OwnerId, Guid? BranchId) : IDataScope;

internal enum AuthSubject { Anonymous, Owner1, Owner2, ManagerA1, ManagerA2, ManagerB1 }

/// <summary>
/// Hai Owner, ba cơ sở, ba quản lý trên một DB riêng; client đăng nhập thật qua /api/v1/auth/login.
/// Owner1 sở hữu A1, A2; Owner2 sở hữu B1; quản lý MA1 giữ A1, MA2 giữ A2, MB1 giữ B1.
/// </summary>
internal sealed class AuthWorld : IAsyncDisposable
{
    public const string Password = "test-password-only";
    private readonly List<HttpClient> _clients = [];

    private AuthWorld(ApiFactory factory, string connectionString)
    {
        Factory = factory;
        ConnectionString = connectionString;
    }

    public ApiFactory Factory { get; }
    public string ConnectionString { get; }
    public Guid Owner1Id { get; private init; }
    public Guid Owner2Id { get; private init; }
    public Guid BranchA1 { get; } = Guid.CreateVersion7();
    public Guid BranchA2 { get; } = Guid.CreateVersion7();
    public Guid BranchB1 { get; } = Guid.CreateVersion7();

    public static string IdentifierOf(AuthSubject subject) => subject switch
    {
        AuthSubject.Owner1 => "owner-one",
        AuthSubject.Owner2 => "owner-two",
        AuthSubject.ManagerA1 => "manager-a1",
        AuthSubject.ManagerA2 => "manager-a2",
        AuthSubject.ManagerB1 => "manager-b1",
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    public static async Task<AuthWorld> CreateAsync(InfrastructureFixture infrastructure)
    {
        string connectionString = await infrastructure.CreateDatabaseAsync();
        ApiFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["RateLimiting:Public:PermitLimit"] = "1000",
        });
        AuthWorld world = new(factory, connectionString) { Owner1Id = Guid.CreateVersion7(), Owner2Id = Guid.CreateVersion7() };
        using IServiceScope scope = factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, IdentifierOf(AuthSubject.Owner1), Password, UserRole.Owner, world.Owner1Id);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, IdentifierOf(AuthSubject.Owner2), Password, UserRole.Owner, world.Owner2Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.Branches.AddRange(
            Branch.Create(world.BranchA1, world.Owner1Id, Details("Cơ sở A1"), now),
            Branch.Create(world.BranchA2, world.Owner1Id, Details("Cơ sở A2"), now),
            Branch.Create(world.BranchB1, world.Owner2Id, Details("Cơ sở B1"), now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, IdentifierOf(AuthSubject.ManagerA1), Password, UserRole.BranchManager, null, world.BranchA1);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, IdentifierOf(AuthSubject.ManagerA2), Password, UserRole.BranchManager, null, world.BranchA2);
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, IdentifierOf(AuthSubject.ManagerB1), Password, UserRole.BranchManager, null, world.BranchB1);
        return world;
    }

    public async Task<HttpClient> ClientForAsync(AuthSubject subject)
    {
        HttpClient client = Factory.CreateClient();
        _clients.Add(client);
        if (subject == AuthSubject.Anonymous) return client;
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = IdentifierOf(subject), password = Password }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Chạy truy vấn bằng DbContext ngoài yêu cầu HTTP, cấp quyền hệ thống tường minh.</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using IServiceScope scope = Factory.Services.CreateSystemScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Mở thêm một DbContext độc lập (cùng DB) với phạm vi dữ liệu chỉ định.</summary>
    public AppDbContext OpenContext(IDataScope scope) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options, scope);

    public async ValueTask DisposeAsync()
    {
        foreach (HttpClient client in _clients) client.Dispose();
        await Factory.DisposeAsync();
    }

    private static BranchDetails Details(string name) =>
        new(name, "1 Đường Thử, Quận 1", "0901234567", 10.77, 106.7, new(6, 0), new(21, 30), 10, [], null, null, null, true);
}
