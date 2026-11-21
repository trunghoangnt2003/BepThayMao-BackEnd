using SkiaSharp;

namespace Bepthaymao.Integration.Tests.Infrastructure;

/// <summary>Ảnh thật cho test upload, tạo bằng SkiaSharp (cùng thư viện với processor, nên chỉ dùng để dựng đầu vào).</summary>
internal static class ImageFixtures
{
    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Webp(int width, int height) => Encode(width, height, SKEncodedImageFormat.Webp);

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using SKBitmap bitmap = new(width, height);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(new SKColor(0xE8, 0xB4, 0x4A));
            using SKPaint paint = new() { Color = new SKColor(0x2B, 0x6C, 0x3E), IsAntialias = true };
            canvas.DrawCircle(width / 3f, height / 2f, Math.Min(width, height) / 4f, paint);
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, 90);
        return data.ToArray();
    }
}
