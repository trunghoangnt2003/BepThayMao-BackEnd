using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Infrastructure.Media;

namespace Bepthaymao.Integration.Tests.Shop;

public sealed class DishImageQuotaTests
{
    [Fact]
    public async Task FullStoreRejectsUploadWithoutDeletingExistingImage()
    {
        string root = Path.Combine(Path.GetTempPath(), "btm-quota-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string existing = Path.Combine(root, "existing.webp");
        try
        {
            using (FileStream stream = File.Create(existing)) stream.SetLength(512L * 1024 * 1024);
            FileSystemDishImageStorage storage = new(new(root));
            await Assert.ThrowsAsync<ConflictException>(() => storage.SaveWebpAsync([1, 2, 3], TestContext.Current.CancellationToken));
            Assert.True(File.Exists(existing));
            Assert.Single(Directory.GetFiles(root, "*.webp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
