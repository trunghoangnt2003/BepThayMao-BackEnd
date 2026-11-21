using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Bepthaymao.Integration.Tests.Infrastructure;

/// <summary>Ảnh thật cho test upload, tạo bằng SkiaSharp (cùng thư viện với processor, nên chỉ dùng để dựng đầu vào).</summary>
internal static class ImageFixtures
{
    private static readonly string _gpsMarker = "GPS 10.7769N 106.7009E";

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Webp(int width, int height) => Encode(width, height, SKEncodedImageFormat.Webp);

    /// <summary>Tệp thực thi Windows đội lốt ảnh: bắt đầu bằng "MZ" rồi rác.</summary>
    public static byte[] NotAnImage() => [.. "MZ"u8, .. new byte[256].Select((_, index) => (byte)(index * 7))];

    /// <summary>GIF 1×1 hợp lệ (43 byte).</summary>
    public static byte[] Gif1x1() =>
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
    ];

    /// <summary>BMP 24 bit 1×1 hợp lệ (58 byte).</summary>
    public static byte[] Bmp1x1()
    {
        byte[] bmp = new byte[58];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), 58);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(34), 4);
        bmp[54] = 0x20;
        bmp[55] = 0x40;
        bmp[56] = 0x80;
        return bmp;
    }

    /// <summary>PNG 1×1 thật nhưng IHDR khai báo kích thước lớn (CRC tính lại): bom điểm ảnh chỉ vài chục byte.</summary>
    public static byte[] PngClaiming(uint width, uint height)
    {
        byte[] png = Png(1, 1);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), Crc32(png.AsSpan(12, 17)));
        return png;
    }

    /// <summary>
    /// JPEG có APP1 EXIF chèn ngay sau SOI: Orientation, ImageDescription chứa chuỗi "GPS ..." và IFD GPS.
    /// Ảnh vẽ circle ở 1/3 chiều ngang; điểm ảnh lưu nguyên (chưa xoay), chỉ thẻ Orientation báo cách hiển thị.
    /// </summary>
    public static byte[] JpegWithExif(int width, int height, ushort orientation)
    {
        byte[] jpeg = Jpeg(width, height);
        byte[] payload = [.. "Exif\0\0"u8, .. Tiff(orientation)];
        byte[] segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), checked((ushort)(payload.Length + 2)));
        payload.CopyTo(segment.AsSpan(4));
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static byte[] Tiff(ushort orientation)
    {
        byte[] description = [.. Encoding.ASCII.GetBytes(_gpsMarker), 0];
        const int Ifd0Size = 2 + (3 * 12) + 4;
        uint descriptionOffset = 8 + Ifd0Size;
        uint gpsOffset = descriptionOffset + (uint)description.Length;

        List<byte> tiff = [.. "MM"u8];
        Be16(tiff, 42);
        Be32(tiff, 8);
        Be16(tiff, 3);
        Entry(tiff, 0x010E, 2, (uint)description.Length, Offset(descriptionOffset));
        Entry(tiff, 0x0112, 3, 1, [(byte)(orientation >> 8), (byte)orientation, 0, 0]);
        Entry(tiff, 0x8825, 4, 1, Offset(gpsOffset));
        Be32(tiff, 0);
        tiff.AddRange(description);
        Be16(tiff, 1);
        Entry(tiff, 0x0001, 2, 2, [(byte)'N', 0, 0, 0]);
        Be32(tiff, 0);
        return [.. tiff];
    }

    private static byte[] Offset(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static void Entry(List<byte> tiff, ushort tag, ushort type, uint count, byte[] value)
    {
        Be16(tiff, tag);
        Be16(tiff, type);
        Be32(tiff, count);
        tiff.AddRange(value);
    }

    private static void Be16(List<byte> target, ushort value) => target.AddRange([(byte)(value >> 8), (byte)value]);

    private static void Be32(List<byte> target, uint value) => target.AddRange(Offset(value));

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

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
