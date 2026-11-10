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
    public void ProblemSchemaPublishesRequiredIdentifiers()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");
        string[] required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        Assert.Contains("correlationId", required);
        Assert.Contains("title", required);
        Assert.Contains("status", required);
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("traceId").GetProperty("type").GetString());
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

    [Fact]
    public void ListPublicBranchesOperationMatchesContract()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/api/v1/branches").GetProperty("get");
        Assert.Equal("listPublicBranches", operation.GetProperty("operationId").GetString());
        Assert.Contains("Branches", operation.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        JsonElement responses = operation.GetProperty("responses");
        JsonElement ok = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("array", ok.GetProperty("type").GetString());
        Assert.Equal("#/components/schemas/PublicBranch", ok.GetProperty("items").GetProperty("$ref").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.True(responses.GetProperty("500").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public void PublicBranchSchemaHasExactlySixRequiredFields()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("PublicBranch");
        string[] expected = ["address", "closeTime", "id", "name", "openTime", "phone"];
        Assert.Equal(expected, schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(expected, schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AllOperationIdsAreUniqueCamelCase()
    {
        string[] ids = [.. Operations().Select(operation => operation.GetProperty("operationId").GetString() ?? "")];
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z][A-Za-z0-9]*$", id));
    }

    [Fact]
    public void AllErrorResponsesUseProblemJson()
    {
        foreach (JsonElement operation in Operations())
        {
            foreach (JsonProperty response in operation.GetProperty("responses").EnumerateObject().Where(r => r.Name[0] is '4' or '5'))
            {
                string[] mediaTypes = [.. response.Value.GetProperty("content").EnumerateObject().Select(c => c.Name)];
                Assert.Equal(["application/problem+json"], mediaTypes);
            }
        }
    }

    [Fact]
    public void ProblemSchemaRequiresCorrelationId()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        JsonElement schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");
        Assert.Contains("correlationId", schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
    }

    private static List<JsonElement> Operations()
    {
        using JsonDocument document = JsonDocument.Parse(ReadCommittedDocument());
        return [.. document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => operation.Value.Clone()))];
    }

    private static string ReadCommittedDocument() =>
        File.ReadAllText(Path.Combine(RepositoryPaths.Root, "openapi", "v1.json"));
}
