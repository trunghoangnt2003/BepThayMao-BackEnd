using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bepthaymao.Api.Security;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Shop;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Security;

/// <summary>Một DB, một bộ client đã đăng nhập cho cả lớp: ma trận có nhiều chục ô nên không dựng lại thế giới mỗi ô.</summary>
public sealed class MatrixWorldFixture : IAsyncLifetime
{
    private readonly InfrastructureFixture _infrastructure = new();
    private readonly Dictionary<string, HttpClient> _clients = [];

    internal AuthWorld World { get; private set; } = null!;
    internal Guid Owner1Dish { get; private set; }
    internal Guid Owner2Dish { get; private set; }
    internal Guid Owner1Banner { get; private set; }

    public HttpClient ClientFor(string subject) => _clients[subject];

    public async ValueTask InitializeAsync()
    {
        await _infrastructure.InitializeAsync();
        World = await AuthWorld.CreateAsync(_infrastructure);
        Owner1Dish = await CreateDishAsync(World.Owner1Id);
        Owner2Dish = await CreateDishAsync(World.Owner2Id);
        Owner1Banner = await CreateBannerAsync(World.Owner1Id);
        foreach (AuthSubject subject in Enum.GetValues<AuthSubject>())
            _clients[subject.ToString()] = await World.ClientForAsync(subject);
    }

    /// <summary>Tạo món mới của Owner (DELETE cần tài nguyên riêng mỗi lần gọi vì xóa mềm không lặp lại được).</summary>
    internal async Task<Guid> CreateDishAsync(Guid ownerId)
    {
        using IServiceScope scope = World.Factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Dish dish = Dish.Create(Guid.CreateVersion7(), ownerId, new("Món ma trận", null, 10000, null, DishCategory.XoiMan, false, 0), DateTimeOffset.UtcNow);
        db.Dishes.Add(dish);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return dish.Id;
    }

    internal async Task<Guid> CreateBannerAsync(Guid ownerId)
    {
        using IServiceScope scope = World.Factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ShopBanner banner = ShopBanner.Create(Guid.CreateVersion7(), ownerId, new("Banner ma trận", null, null, null, 0, true), DateTimeOffset.UtcNow);
        db.ShopBanners.Add(banner);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return banner.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await World.DisposeAsync();
        await _infrastructure.DisposeAsync();
    }
}

/// <summary>
/// SC-006: ma trận endpoint × chủ thể. Bảng endpoint là nguồn kỳ vọng; test đối chiếu nó với EndpointDataSource
/// nên thêm endpoint được bảo vệ mà quên ma trận thì đỏ.
/// </summary>
public sealed partial class AuthorizationMatrixTests(MatrixWorldFixture fixture) : IClassFixture<MatrixWorldFixture>
{
    private sealed record Endpoint(string Method, string Template, bool OwnerArea, bool PerBranch, int SuccessStatus, bool Owner1Resource = false)
    {
        public string Key => $"{Method} {Template}";
    }

    private static readonly Endpoint[] _endpoints =
    [
        new("GET", "/owner/branches", true, false, 200),
        new("POST", "/owner/branches", true, false, 201),
        new("GET", "/owner/branches/{branchId}", true, true, 200),
        new("PUT", "/owner/branches/{branchId}", true, true, 200),
        new("POST", "/owner/branches/{branchId}/manager/reset-password", true, true, 204),
        new("GET", "/manager/branch/settings", false, false, 200),
        new("PUT", "/manager/branch/settings", false, false, 200),
        new("GET", "/owner/dishes", true, false, 200),
        new("POST", "/owner/dishes", true, false, 201),
        new("PUT", "/owner/dishes/{dishId}", true, false, 200, Owner1Resource: true),
        new("DELETE", "/owner/dishes/{dishId}", true, false, 204, Owner1Resource: true),
        new("GET", "/owner/branches/{branchId}/dishes", true, true, 200),
        new("PUT", "/owner/branches/{branchId}/dishes/{dishId}/availability", true, true, 200),
        new("GET", "/owner/shop-content/banners", true, false, 200),
        new("POST", "/owner/shop-content/banners", true, false, 201),
        new("PUT", "/owner/shop-content/banners/{bannerId}", true, false, 200, Owner1Resource: true),
        new("DELETE", "/owner/shop-content/banners/{bannerId}", true, false, 204, Owner1Resource: true),
        new("PUT", "/owner/shop-content/about", true, false, 200),
        new("GET", "/owner/shop-content/legal", true, false, 200),
        new("PUT", "/owner/shop-content/legal", true, false, 200),
        new("PUT", "/owner/shop-content/policies/{kind}", true, false, 200),
        new("GET", "/owner/orders", true, false, 200),
        new("GET", "/manager/dishes", false, false, 200),
        new("PUT", "/manager/dishes/{dishId}/availability", false, false, 200),
        new("GET", "/manager/orders", false, false, 200),
        new("GET", "/owner/overview", true, false, 200),
        new("GET", "/manager/overview", false, false, 200),
    ];

    private static readonly string[] _subjects = ["Anonymous", "Owner1", "Owner2", "ManagerA1", "ManagerA2", "ManagerB1"];
    private static readonly string[] _branches = ["A1", "A2", "B1"];

    // Chủ sở hữu thật của từng cơ sở: Owner1 giữ A1 và A2, Owner2 giữ B1.
    private static readonly Dictionary<string, string> _ownerOfBranch = new() { ["A1"] = "Owner1", ["A2"] = "Owner1", ["B1"] = "Owner2" };

    private static int _createdManagers;

    private static int Expected(Endpoint endpoint, string subject, string branch)
    {
        if (subject == "Anonymous") return 401;
        if (endpoint.OwnerArea != subject.StartsWith("Owner", StringComparison.Ordinal)) return 403;
        // Món/banner thuộc Owner1: Owner2 nhận 404 (không lộ sự tồn tại).
        if (endpoint.Owner1Resource) return subject == "Owner1" ? endpoint.SuccessStatus : 404;
        if (!endpoint.PerBranch) return endpoint.SuccessStatus;
        return _ownerOfBranch[branch] == subject ? endpoint.SuccessStatus : 404;
    }

    private static IEnumerable<(string Endpoint, string Subject, string Branch, int Expected)> AllCells() =>
        from endpoint in _endpoints
        from subject in _subjects
        from branch in endpoint.PerBranch ? _branches : [""]
        select (endpoint.Key, subject, branch, Expected(endpoint, subject, branch));

    public static TheoryData<string, string, string, int> Cells()
    {
        TheoryData<string, string, string, int> cells = [];
        foreach ((string endpoint, string subject, string branch, int expected) in AllCells())
            cells.Add(endpoint, subject, branch, expected);
        return cells;
    }

    public static TheoryData<string, string, string, int> SuccessfulCells()
    {
        TheoryData<string, string, string, int> cells = [];
        foreach ((string endpoint, string subject, string branch, int expected) in AllCells().Where(cell => cell.Expected is >= 200 and < 300))
            cells.Add(endpoint, subject, branch, expected);
        return cells;
    }

    private Guid IdOf(string branch) => branch switch
    {
        "A1" => fixture.World.BranchA1,
        "A2" => fixture.World.BranchA2,
        "B1" => fixture.World.BranchB1,
        _ => Guid.Empty,
    };

    private static string NameOf(string branch) => $"Cơ sở {branch}";

    /// <summary>Món dùng cho ô ma trận: món của Owner1, hoặc món của chủ cơ sở (theo route hoặc theo cơ sở của quản lý).</summary>
    private async Task<Guid> DishFor(Endpoint endpoint, string subject, string branch)
    {
        if (endpoint.Owner1Resource) return endpoint.Method == "DELETE" ? await fixture.CreateDishAsync(fixture.World.Owner1Id) : fixture.Owner1Dish;
        string ownerBranch = endpoint.PerBranch ? branch : subject switch { "ManagerB1" => "B1", _ => "A1" };
        return _ownerOfBranch[ownerBranch] == "Owner2" ? fixture.Owner2Dish : fixture.Owner1Dish;
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(string endpointKey, string subject, string branch)
    {
        Endpoint endpoint = _endpoints.Single(value => value.Key == endpointKey);
        string path = "/api/v1" + endpoint.Template.Replace("{branchId}", IdOf(branch).ToString(), StringComparison.Ordinal);
        if (path.Contains("{dishId}", StringComparison.Ordinal))
            path = path.Replace("{dishId}", (await DishFor(endpoint, subject, branch)).ToString(), StringComparison.Ordinal);
        path = path.Replace("{kind}", "privacy", StringComparison.Ordinal);
        if (path.Contains("{bannerId}", StringComparison.Ordinal))
            path = path.Replace("{bannerId}", (endpoint.Method == "DELETE" ? await fixture.CreateBannerAsync(fixture.World.Owner1Id) : fixture.Owner1Banner).ToString(), StringComparison.Ordinal);
        using HttpRequestMessage request = new(new HttpMethod(endpoint.Method), path);
        if (endpoint.Method != "GET") request.Content = JsonContent.Create(BodyFor(endpoint, branch));
        using HttpResponseMessage response = await fixture.ClientFor(subject).SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // Thân luôn hợp lệ và không đổi dữ liệu mẫu: một ô lệch quyền không được phép trả 400 che lỗi phân quyền.
    private static JsonObject BodyFor(Endpoint endpoint, string branch) => endpoint.Key switch
    {
        "POST /owner/branches" => new JsonObject
        {
            ["branch"] = OwnerSettings("Cơ sở mới"),
            ["manager"] = new JsonObject { ["identifier"] = $"quan-ly-ma-tran-{Interlocked.Increment(ref _createdManagers)}", ["password"] = "mat-khau-quan-ly-12" },
        },
        "PUT /owner/branches/{branchId}" => OwnerSettings(NameOf(branch)),
        "POST /owner/branches/{branchId}/manager/reset-password" => new JsonObject { ["newPassword"] = "mat-khau-moi-ma-tran-12" },
        "POST /owner/dishes" or "PUT /owner/dishes/{dishId}" => new JsonObject { ["name"] = "Món ma trận", ["price"] = 10000, ["category"] = "XoiMan" },
        "PUT /owner/branches/{branchId}/dishes/{dishId}/availability" or "PUT /manager/dishes/{dishId}/availability" => new JsonObject { ["isAvailable"] = true },
        "POST /owner/shop-content/banners" or "PUT /owner/shop-content/banners/{bannerId}" => new JsonObject { ["title"] = "Banner ma trận", ["sortOrder"] = 0, ["isActive"] = true },
        "PUT /owner/shop-content/about" => new JsonObject { ["about"] = "Giới thiệu ma trận" },
        "PUT /owner/shop-content/legal" => new JsonObject { ["businessName"] = "Hộ kinh doanh ma trận" },
        "PUT /owner/shop-content/policies/{kind}" => new JsonObject { ["body"] = "Chính sách ma trận" },
        _ => new JsonObject
        {
            ["phone"] = "0901234567",
            ["openTime"] = "06:00",
            ["closeTime"] = "21:30",
            ["abandonedThresholdMinutes"] = 10,
            ["zaloRecipientIds"] = new JsonArray(),
        },
    };

    private static JsonObject OwnerSettings(string name) => new()
    {
        ["name"] = name,
        ["address"] = "1 Đường Thử, Quận 1",
        ["phone"] = "0901234567",
        ["lat"] = 10.77,
        ["lng"] = 106.7,
        ["openTime"] = "06:00",
        ["closeTime"] = "21:30",
        ["abandonedThresholdMinutes"] = 10,
        ["zaloRecipientIds"] = new JsonArray(),
        ["isActive"] = true,
    };

    [GeneratedRegex(@"\{(\w+):[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteConstraint();

    private static string Normalize(string rawPattern)
    {
        string path = rawPattern.Replace("/api/v{version:apiVersion}", "", StringComparison.Ordinal);
        path = RouteConstraint().Replace(path, "{$1}");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    [Fact]
    public void EveryProtectedEndpointIsInMatrix()
    {
        EndpointDataSource source = fixture.World.Factory.Services.GetRequiredService<EndpointDataSource>();
        string[] protectedEndpoints = [.. source.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["?"])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText!)}"))
            .Order(StringComparer.Ordinal)];
        Assert.Equal(_endpoints.Select(endpoint => endpoint.Key).Order(StringComparer.Ordinal), protectedEndpoints);
    }

    [Fact]
    public void MatrixCoversEverySubjectOnEveryEndpoint()
    {
        Assert.Equal(_endpoints.Sum(endpoint => _subjects.Length * (endpoint.PerBranch ? _branches.Length : 1)), AllCells().Count());
        Assert.True(AllCells().Count() > 50);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Matrix(string endpointKey, string subject, string branch, int expected)
    {
        (HttpStatusCode status, string body) = await SendAsync(endpointKey, subject, branch);
        Assert.True((int)status == expected, $"{subject} {endpointKey} (cơ sở '{branch}'): mong {expected}, nhận {(int)status}: {body}");
    }

    [Theory]
    [MemberData(nameof(SuccessfulCells))]
    public async Task NoCrossBranchDataInSuccessfulResponses(string endpointKey, string subject, string branch, int expected)
    {
        (HttpStatusCode status, string body) = await SendAsync(endpointKey, subject, branch);
        Assert.True((int)status == expected, $"{subject} {endpointKey} (cơ sở '{branch}'): mong {expected}, nhận {(int)status}: {body}");
        string[] visible = subject switch
        {
            "Owner1" => ["A1", "A2"],
            "Owner2" => ["B1"],
            "ManagerA1" => ["A1"],
            "ManagerA2" => ["A2"],
            _ => ["B1"],
        };
        foreach (string foreign in _branches.Except(visible))
        {
            Assert.DoesNotContain(IdOf(foreign).ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(NameOf(foreign), body, StringComparison.Ordinal);
        }
        foreach (Guid foreignOwner in new[] { fixture.World.Owner1Id, fixture.World.Owner2Id })
            Assert.DoesNotContain(foreignOwner.ToString(), body, StringComparison.OrdinalIgnoreCase);
        if (endpointKey is "GET /manager/branch/settings" or "PUT /manager/branch/settings")
            Assert.Contains(NameOf(visible[0]), body, StringComparison.Ordinal);
    }
}
