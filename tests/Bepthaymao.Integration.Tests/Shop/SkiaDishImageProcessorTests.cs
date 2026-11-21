using System.Text;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Infrastructure.Media;
using Bepthaymao.Integration.Tests.Infrastructure;
using SkiaSharp;

namespace Bepthaymao.Integration.Tests.Shop;

/// <summary>Bộ xử lý ảnh món (D-10): định dạng theo nội dung, chặn bom điểm ảnh, xoay EXIF rồi bỏ metadata, không phóng to.</summary>
public sealed class SkiaDishImageProcessorTests
{
    private readonly SkiaDishImageProcessor _processor = new();

    [Fact]
    public void RejectsNonImageEvenWithJpegName() =>
        Assert.Throws<UnsupportedMediaTypeException>(() => _processor.ToWebp(new MemoryStream(ImageFixtures.NotAnImage())));

    [Fact]
    public void RejectsGifAndBmp()
    {
        Assert.Throws<UnsupportedMediaTypeException>(() => _processor.ToWebp(new MemoryStream(ImageFixtures.Gif1x1())));
        Assert.Throws<UnsupportedMediaTypeException>(() => _processor.ToWebp(new MemoryStream(ImageFixtures.Bmp1x1())));
    }

    [Fact]
    public void RejectsPixelBombBeforeDecoding() =>
        Assert.Throws<PayloadTooLargeException>(() => _processor.ToWebp(new MemoryStream(ImageFixtures.PngClaiming(30000, 30000))));

    [Fact]
    public void AppliesExifOrientationThenStripsMetadata()
    {
        byte[] input = ImageFixtures.JpegWithExif(400, 200, 6);
        using (SKCodec source = SKCodec.Create(new MemoryStream(input)))
            Assert.Equal(SKEncodedOrigin.RightTop, source.EncodedOrigin);

        byte[] output = _processor.ToWebp(new MemoryStream(input));

        using SKBitmap decoded = SKBitmap.Decode(output);
        Assert.Equal(200, decoded.Width);
        Assert.Equal(400, decoded.Height);
        // Tâm vòng tròn gốc (133,100) xoay 90 độ theo chiều kim đồng hồ thành (100,133): màu xanh lá, không phải nền vàng.
        Assert.True(decoded.GetPixel(100, 133).Red < 120, "Vòng tròn phải nằm ở nửa trên sau khi xoay RightTop.");
        Assert.True(decoded.GetPixel(100, 267).Red > 180, "Nửa dưới là nền vàng.");

        string text = Encoding.Latin1.GetString(output);
        Assert.DoesNotContain("EXIF", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("XMP ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GPS", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsAspectRatioWhenShrinking()
    {
        using SKBitmap big = SKBitmap.Decode(_processor.ToWebp(new MemoryStream(ImageFixtures.Png(3000, 1500))));
        Assert.Equal((1200, 600), (big.Width, big.Height));

        using SKBitmap small = SKBitmap.Decode(_processor.ToWebp(new MemoryStream(ImageFixtures.Webp(500, 500))));
        Assert.Equal((500, 500), (small.Width, small.Height));
    }
}
