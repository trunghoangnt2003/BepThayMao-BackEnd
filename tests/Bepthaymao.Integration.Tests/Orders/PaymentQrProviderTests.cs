using Bepthaymao.Domain.Branches;
using Bepthaymao.Infrastructure.Payments;

namespace Bepthaymao.Integration.Tests.Orders;

public sealed class PaymentQrProviderTests
{
    [Fact]
    public void SepayUsesOfficialImageHostAndEscapesEveryField()
    {
        string url = new VietQrService().CreateQrUrl("970436", "000123", 50000, "BTM123456", QrProvider.SePay);
        Assert.Equal("https://vietqr.app/img?acc=000123&bank=970436&amount=50000&des=BTM123456&template=qronly", url);
        string escaped = new VietQrService().CreateQrUrl("A&B", "012?x=1", 1, "BTM&x=1", QrProvider.SePay);
        Assert.Contains("bank=A%26B", escaped, StringComparison.Ordinal);
        Assert.Contains("des=BTM%26x%3D1", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultRemainsLocalVietQrAndUnknownProviderIsRejected()
    {
        VietQrService service = new();
        Assert.StartsWith("data:image/svg+xml;base64,", service.CreateQrUrl("970436", "000123", 25000, "BTM123456"), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateQrUrl("970436", "000123", 1, "BTM123456", (QrProvider)99));
    }
}
