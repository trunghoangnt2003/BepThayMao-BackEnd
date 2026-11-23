using System.Net;
using System.Net.Http.Headers;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.Health;

public sealed class MapFilesTests
{
    [Fact]
    public async Task PublicArchiveSupportsRangesAndRejectsOutOfBoundsAndUnknownFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "btm-maps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "vietnam.pmtiles"), new byte[256], TestContext.Current.CancellationToken);
            await using ApiFactory factory = new(new Dictionary<string, string?> { ["Maps:RootPath"] = root });
            using HttpClient client = factory.CreateClient();
            using HttpRequestMessage request = new(HttpMethod.Get, "/maps/vietnam.pmtiles");
            request.Headers.Range = new RangeHeaderValue(0, 126);
            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("bytes 0-126/256", response.Content.Headers.ContentRange?.ToString());
            Assert.Equal(127, (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);
            using HttpRequestMessage outside = new(HttpMethod.Get, "/maps/vietnam.pmtiles");
            outside.Headers.Range = new RangeHeaderValue(256, 257);
            using HttpResponseMessage invalid = await client.SendAsync(outside, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, invalid.StatusCode);
            using HttpResponseMessage missing = await client.GetAsync("/maps/secrets.json", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
