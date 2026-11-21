using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Menu;
using SkiaSharp;

namespace Bepthaymao.Infrastructure.Media;

internal sealed class SkiaDishImageProcessor : IDishImageProcessor
{
    public byte[] ToWebp(Stream content)
    {
        // Định dạng lấy từ nội dung byte (SKCodec), không tin Content-Type hay đuôi tên (T-02-08).
        using SKCodec codec = SKCodec.Create(content) ?? throw NotSupported();
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw NotSupported();

        // Kích thước đọc từ header: từ chối bom điểm ảnh trước khi cấp phát bitmap (T-02-11).
        if ((long)codec.Info.Width * codec.Info.Height > DishImageRules.MaxPixels)
            throw new PayloadTooLargeException("Ảnh có quá nhiều điểm ảnh. Chọn ảnh nhỏ hơn.");

        // Dispose hai lần cùng một đối tượng (khi bước không đổi gì) là an toàn với SkiaSharp.
        using SKBitmap decoded = SKBitmap.Decode(codec) ?? throw NotSupported();
        using SKBitmap resized = Shrink(decoded);
        using SKBitmap upright = Orient(resized, codec.EncodedOrigin);

        // Mã hóa từ bitmap thuần: không mang theo EXIF/XMP/GPS của tệp gốc (T-02-12).
        using SKImage image = SKImage.FromBitmap(upright);
        using SKData webp = image.Encode(SKEncodedImageFormat.Webp, DishImageRules.WebpQuality)
            ?? throw new InvalidOperationException("Không mã hóa được ảnh WebP.");
        return webp.ToArray();
    }

    private static UnsupportedMediaTypeException NotSupported() => new("Chỉ nhận ảnh JPEG, PNG hoặc WebP.");

    /// <summary>Chỉ thu nhỏ, giữ tỉ lệ theo cạnh dài; không phóng to, không cắt. Không đổi gì thì trả chính <paramref name="source"/>.</summary>
    private static SKBitmap Shrink(SKBitmap source)
    {
        int longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= DishImageRules.MaxEdge) return source;
        double scale = (double)DishImageRules.MaxEdge / longEdge;
        SKImageInfo info = source.Info.WithSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Không thu nhỏ được ảnh.");
    }

    /// <summary>Áp phép xoay/lật theo <see cref="SKEncodedOrigin"/> vào điểm ảnh, vì metadata sẽ bị bỏ (Pitfall 2).</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source;
        int w = source.Width;
        int h = source.Height;
        bool swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        SKBitmap target = new(source.Info.WithSize(swapsAxes ? h : w, swapsAxes ? w : h));
        using SKCanvas canvas = new(target);
        canvas.SetMatrix(OriginMatrix(origin, w, h));
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return target;
    }

    // Ma trận trùng SkEncodedOriginToMatrix của Skia: ánh xạ ảnh nguồn w×h sang hướng hiển thị đúng.
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
