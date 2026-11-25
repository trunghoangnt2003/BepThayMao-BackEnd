using Bepthaymao.Application.Menu;
using Bepthaymao.Application.Abstractions.Errors;

namespace Bepthaymao.Infrastructure.Media;

/// <summary>Thư mục gốc ảnh món đã resolve thành đường dẫn tuyệt đối (ghi và phục vụ dùng chung một giá trị).</summary>
public sealed record DishImageStorageOptions(string RootPath);

internal sealed class FileSystemDishImageStorage(DishImageStorageOptions options) : IDishImageStorage
{
    public async Task<string> SaveWebpAsync(byte[] webp, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(root);
        // Một nửa PVC 1 GiB: cả ảnh mồ côi vẫn tính vào quota, không xóa dữ liệu của Owner.
        await using FileStream storageLock = await AcquireLockAsync(root, cancellationToken);
        long used = Directory.EnumerateFiles(root).Sum(path => new FileInfo(path).Length);
        if (used > 512L * 1024 * 1024 - webp.LongLength)
            throw new ConflictException("file", "Kho ảnh đã đầy. Liên hệ chủ quán để kiểm tra dung lượng.");
        string fileName = Guid.CreateVersion7().ToString("N") + ".webp";
        string target = Path.GetFullPath(Path.Combine(root, fileName));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Đường dẫn ảnh nằm ngoài thư mục lưu trữ.");
        string temp = Path.Combine(root, "." + fileName + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, webp, cancellationToken);
            File.Move(temp, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return DishImageRules.PathPrefix + fileName;
    }
    private static async Task<FileStream> AcquireLockAsync(string root, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, ".storage.lock");
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
