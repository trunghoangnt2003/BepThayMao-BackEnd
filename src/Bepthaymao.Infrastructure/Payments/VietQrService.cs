using System.Globalization;
using System.Text;
using Bepthaymao.Application.Orders;
using QRCoder;

namespace Bepthaymao.Infrastructure.Payments;

/// <summary>
/// Dựng chuỗi VietQR (EMVCo, NAPAS 247 chuyển nhanh tới tài khoản) và vẽ SVG nội bộ (D-07): không gọi dịch vụ ngoài.
/// Số tiền = tiền món; nội dung chuyển khoản = mã đơn.
/// </summary>
internal sealed class VietQrService : IPaymentQrService
{
    public string CreateQrUrl(string bankCode, string accountNo, long amount, string transferContent)
    {
        string payload = BuildPayload(bankCode, accountNo, amount, transferContent);
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        string svg = new SvgQRCode(data).GetGraphic(8);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    internal static string BuildPayload(string bankCode, string accountNo, long amount, string transferContent)
    {
        string beneficiary = Field("00", bankCode.Trim()) + Field("01", accountNo.Trim());
        string merchant = Field("00", "A000000727") + Field("01", beneficiary) + Field("02", "QRIBFTTA");
        string body = Field("00", "01") + Field("01", "12") + Field("38", merchant) + Field("53", "704")
            + Field("54", amount.ToString(CultureInfo.InvariantCulture)) + Field("58", "VN") + Field("62", Field("08", transferContent))
            + "6304";
        return body + Crc16(body).ToString("X4", CultureInfo.InvariantCulture);
    }

    private static string Field(string id, string value) => id + value.Length.ToString("D2", CultureInfo.InvariantCulture) + value;

    /// <summary>CRC-16/CCITT-FALSE (đa thức 0x1021, khởi tạo 0xFFFF) theo EMVCo.</summary>
    internal static ushort Crc16(string text)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in Encoding.ASCII.GetBytes(text))
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
