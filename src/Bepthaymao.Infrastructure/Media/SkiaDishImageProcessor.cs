using Bepthaymao.Application.Menu;
using SkiaSharp;

namespace Bepthaymao.Infrastructure.Media;

internal sealed class SkiaDishImageProcessor : IDishImageProcessor
{
    public byte[] ToWebp(Stream content)
    {
        using SKCodec codec = SKCodec.Create(content);
        using SKBitmap decoded = SKBitmap.Decode(codec);
        using SKBitmap resized = Shrink(decoded);
        using SKImage image = SKImage.FromBitmap(resized);
        using SKData webp = image.Encode(SKEncodedImageFormat.Webp, DishImageRules.WebpQuality);
        return webp.ToArray();
    }

    /// <summary>Chỉ thu nhỏ, giữ tỉ lệ theo cạnh dài; không phóng to, không cắt.</summary>
    private static SKBitmap Shrink(SKBitmap source)
    {
        int longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= DishImageRules.MaxEdge) return source.Copy();
        double scale = (double)DishImageRules.MaxEdge / longEdge;
        SKImageInfo info = source.Info.WithSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Không thu nhỏ được ảnh.");
    }
}
