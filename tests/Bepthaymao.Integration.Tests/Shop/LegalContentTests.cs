using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Shop;

/// <summary>Chân trang theo quy định thương mại điện tử: thông tin người bán và chính sách do chủ quán nhập.</summary>
public sealed class LegalContentTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const string _password = "mat-khau-kiem-thu-12";
    private ApiFactory _factory = null!;

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
        await AuthTestData.CreateUserAsync(scope.ServiceProvider, "chu-quan", _password, UserRole.Owner, Guid.CreateVersion7());
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task PublicFooterShowsOnlyWhatTheOwnerEntered()
    {
        HttpClient guest = _factory.CreateClient();
        JsonElement empty = await GetJsonAsync(guest, "/api/v1/shop/legal");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("businessName").ValueKind);
        Assert.Equal(0, empty.GetProperty("policies").GetArrayLength());

        HttpClient owner = await LoginAsync();
        using HttpResponseMessage saved = await owner.PutAsJsonAsync("/api/v1/owner/shop-content/legal", new
        {
            businessName = "Hộ kinh doanh Bếp Thầy Mão",
            registrationNumber = "01A8000000",
            registrationIssuedOn = "2024-03-01",
            registrationIssuedBy = "UBND quận Mẫu",
            address = "Số 1 Đường Mẫu",
            phone = "0900000000",
            email = "lienhe@vidu.vn",
            moitNoticeUrl = "https://online.gov.vn/Home/WebDetails/1",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        (await owner.PutAsJsonAsync("/api/v1/owner/shop-content/policies/privacy", new { body = "Chúng tôi chỉ dùng số điện thoại để giao đơn." },
            TestContext.Current.CancellationToken)).Dispose();
        (await owner.PutAsJsonAsync("/api/v1/owner/shop-content/policies/refund", new { body = "   " },
            TestContext.Current.CancellationToken)).Dispose();

        JsonElement footer = await GetJsonAsync(guest, "/api/v1/shop/legal");
        Assert.Equal("Hộ kinh doanh Bếp Thầy Mão", footer.GetProperty("businessName").GetString());
        Assert.Equal("2024-03-01", footer.GetProperty("registrationIssuedOn").GetString());
        JsonElement policy = Assert.Single(footer.GetProperty("policies").EnumerateArray());
        Assert.Equal("Privacy", policy.GetProperty("kind").GetString());
        Assert.Equal("Chính sách bảo mật thông tin", policy.GetProperty("title").GetString());

        JsonElement page = await GetJsonAsync(guest, "/api/v1/shop/policies/Privacy");
        Assert.Equal("Chúng tôi chỉ dùng số điện thoại để giao đơn.", page.GetProperty("body").GetString());
        using HttpResponseMessage hidden = await guest.GetAsync("/api/v1/shop/policies/refund", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using HttpResponseMessage unknown = await guest.GetAsync("/api/v1/shop/policies/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        JsonElement mine = await GetJsonAsync(owner, "/api/v1/owner/shop-content/legal");
        Assert.Equal(6, mine.GetProperty("policies").GetArrayLength());
    }

    [Theory]
    [InlineData("http://online.gov.vn/x")]
    [InlineData("https://online.gov.vn.trang-gia.vn/x")]
    [InlineData("khong-phai-duong-dan")]
    public async Task NoticeLinkMustPointToTheMinistrySite(string url)
    {
        HttpClient owner = await LoginAsync();
        using HttpResponseMessage response = await owner.PutAsJsonAsync("/api/v1/owner/shop-content/legal", new { moitNoticeUrl = url },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidEmailIsRejected()
    {
        HttpClient owner = await LoginAsync();
        using HttpResponseMessage response = await owner.PutAsJsonAsync("/api/v1/owner/shop-content/legal", new { email = "khong-co-a-cong" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> LoginAsync()
    {
        HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = "chu-quan", password = _password },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
