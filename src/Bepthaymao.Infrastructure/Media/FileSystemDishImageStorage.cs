using Bepthaymao.Application.Menu;

namespace Bepthaymao.Infrastructure.Media;

/// <summary>Thư mục gốc ảnh món đã resolve thành đường dẫn tuyệt đối (ghi và phục vụ dùng chung một giá trị).</summary>
public sealed record DishImageStorageOptions(string RootPath);

internal sealed class FileSystemDishImageStorage(DishImageStorageOptions options) : IDishImageStorage
{
    public async Task<string> SaveWebpAsync(byte[] webp, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(root);
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
}
