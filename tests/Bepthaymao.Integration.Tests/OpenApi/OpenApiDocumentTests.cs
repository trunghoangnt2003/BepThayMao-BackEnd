using System.Text.Json;
using Bepthaymao.Integration.Tests.Infrastructure;

namespace Bepthaymao.Integration.Tests.OpenApi;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public void CommittedDocumentHasContractInfo()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        JsonElement root = document.RootElement;
        JsonElement info = root.GetProperty("info");
        Assert.StartsWith("3.1", root.GetProperty("openapi").GetString());
        Assert.Equal("Bepthaymao Ordering API", info.GetProperty("title").GetString());
        Assert.Equal("1.0.0", info.GetProperty("version").GetString());
        Assert.Equal("Đội phát triển Bếp Thầy Mão", info.GetProperty("contact").GetProperty("name").GetString());
        Assert.Equal("/", root.GetProperty("servers")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void BearerSchemeIsDeclaredAndOnlyProtectedOperationsRequireIt()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        Assert.True(document.RootElement.GetProperty("components").TryGetProperty("securitySchemes", out JsonElement schemes), "Thiếu components.securitySchemes.");
        Assert.True(schemes.TryGetProperty("bearerAuth", out JsonElement scheme), "Thiếu security scheme bearerAuth.");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
        Dictionary<string, bool> secured = [];
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
            foreach (JsonProperty operation in path.Value.EnumerateObject())
                secured[operation.Value.GetProperty("operationId").GetString()!] = operation.Value.TryGetProperty("security", out JsonElement security)
                    && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("bearerAuth", out _));
        foreach (string name in new[] { "createOwnerBranch", "listOwnerBranches", "getOwnerBranch" })
            Assert.True(secured[name], $"{name} phải yêu cầu bearerAuth.");
        foreach (string name in new[] { "login", "refreshToken", "logout", "listPublicBranches" })
            Assert.False(secured[name], $"{name} không được yêu cầu bearerAuth.");
    }

    [Fact]
    public async Task RuntimeDocumentMatchesCommittedFile()
    {
        await using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        string actual = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ReadCommittedDocument().Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            actual.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
    }

    private static string ReadCommittedDocument() =>
        File.ReadAllText(Path.Combine(RepositoryPaths.Root, "openapi", "v1.json"));
}
