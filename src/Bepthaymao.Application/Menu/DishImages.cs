using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Security;

namespace Bepthaymao.Application.Menu;

/// <summary>Giới hạn và quy ước ảnh món (D-10). "2 MB" hiểu là 2 MiB (A-04-1).</summary>
public static class DishImageRules
{
    public const long MaxBytes = 2_097_152;
    public const long MultipartLimit = 3_145_728;
    public const long MaxPixels = 40_000_000;
    public const int MaxEdge = 1200;
    public const int WebpQuality = 80;
    public const string PathPrefix = "/media/mon/";
}

public sealed record UploadedImageDto(string ImageUrl);

/// <summary>Tải một ảnh món; chưa gắn vào món nào. <see cref="Length"/> là cỡ khai báo của tệp, kiểm trước khi đọc nội dung.</summary>
public sealed record UploadOwnerDishImageCommand(Stream Content, long Length) : ICommand<UploadedImageDto>;

/// <summary>Giải mã, xoay theo EXIF, thu nhỏ và mã hóa lại thành WebP không metadata.</summary>
public interface IDishImageProcessor
{
    byte[] ToWebp(Stream content);
}

/// <summary>Lưu WebP với tên do server sinh, trả đường dẫn trên site.</summary>
public interface IDishImageStorage
{
    Task<string> SaveWebpAsync(byte[] webp, CancellationToken cancellationToken);
}

internal sealed class UploadOwnerDishImageCommandHandler(ICurrentUser currentUser, IDishImageProcessor processor, IDishImageStorage storage)
    : ICommandHandler<UploadOwnerDishImageCommand, UploadedImageDto>
{
    public async Task<UploadedImageDto> Handle(UploadOwnerDishImageCommand command, CancellationToken cancellationToken)
    {
        _ = currentUser.OwnerId ?? throw new ForbiddenException();
        byte[] webp = processor.ToWebp(command.Content);
        return new(await storage.SaveWebpAsync(webp, cancellationToken));
    }
}
