using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Shop;

/// <summary>Owner tải ảnh món (D-10): server đặt tên, mã hóa lại thành WebP và phục vụ tĩnh ở /media/mon/.</summary>
public sealed class DishImageUploadTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private AuthWorld _world = null!;

    public async ValueTask InitializeAsync() => _world = await AuthWorld.CreateAsync(infrastructure);

    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    [Fact]
    public async Task OwnerUploadsJpegAndGetsServableWebp()
    {
        HttpClient owner = await _world.ClientForAsync(AuthSubject.Owner1);
        using HttpResponseMessage created = await UploadAsync(owner, ImageFixtures.Jpeg(800, 600), "mon.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        string imageUrl = await ReadImageUrlAsync(created);
        Assert.Matches(new Regex(@"^/media/mon/[0-9a-f]{32}\.webp$", RegexOptions.None, TimeSpan.FromSeconds(1)), imageUrl);

        using HttpResponseMessage served = await owner.GetAsync(imageUrl, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/webp", served.Content.Headers.ContentType?.MediaType);
        string cacheControl = served.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("immutable", cacheControl, StringComparison.Ordinal);
        Assert.Contains("max-age=31536000", cacheControl, StringComparison.Ordinal);
        byte[] bytes = await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
    }

    [Fact]
    public async Task UploadedUrlIsAcceptedWhenSavingDish()
    {
        HttpClient owner = await _world.ClientForAsync(AuthSubject.Owner1);
        using HttpResponseMessage created = await UploadAsync(owner, ImageFixtures.Png(640, 480), "mon.png", "image/png");
        string imageUrl = await ReadImageUrlAsync(created);
        Guid dishId = await CreateDishAsync(owner, imageUrl: null);

        using HttpResponseMessage saved = await owner.PutAsJsonAsync($"/api/v1/owner/dishes/{dishId}",
            new { name = "Xôi gà", price = 30000, category = "XoiMan", imageUrl }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using HttpResponseMessage menu = await _world.Factory.CreateClient().GetAsync($"/api/v1/menu/{dishId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, menu.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await menu.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(imageUrl, json.RootElement.GetProperty("imageUrl").GetString());
    }

    [Fact]
    public async Task UploadDoesNotChangeAnyDish()
    {
        HttpClient owner = await _world.ClientForAsync(AuthSubject.Owner1);
        Guid dishId = await CreateDishAsync(owner, "/images/mon/cu.jpg");

        using HttpResponseMessage created = await UploadAsync(owner, ImageFixtures.Webp(500, 500), "mon.webp", "image/webp");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage list = await owner.GetAsync("/api/v1/owner/dishes", TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement dish = Assert.Single(json.RootElement.EnumerateArray(), item => item.GetProperty("id").GetGuid() == dishId);
        Assert.Equal("/images/mon/cu.jpg", dish.GetProperty("imageUrl").GetString());
    }

    internal static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName, string contentType)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/v1/owner/dish-images", form, TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadImageUrlAsync(HttpResponseMessage response)
    {
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("imageUrl").GetString()!;
    }

    private static async Task<Guid> CreateDishAsync(HttpClient owner, string? imageUrl)
    {
        using HttpResponseMessage response = await owner.PostAsJsonAsync("/api/v1/owner/dishes",
            new { name = "Xôi thử ảnh", price = 25000, category = "XoiMan", imageUrl }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
