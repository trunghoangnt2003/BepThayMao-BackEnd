using System.Text.Json;
using System.Text.Json.Serialization;
using Bepthaymao.Application.Branches.UpsertBranches;

namespace Bepthaymao.Infrastructure.Seeding;

public static class BranchSeedFile
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static async Task<IReadOnlyList<BranchUpsertItem>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        SeedDocument document = await JsonSerializer.DeserializeAsync<SeedDocument>(stream, _options, cancellationToken)
            ?? throw new JsonException("Tệp dữ liệu không hợp lệ.");
        return document.Branches;
    }

    private sealed record SeedDocument(IReadOnlyList<BranchUpsertItem> Branches);
}
