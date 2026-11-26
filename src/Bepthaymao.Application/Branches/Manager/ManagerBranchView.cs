using System.Globalization;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.Manager;

/// <summary>Schema <c>ManagerBranchView</c>: bốn trường được sửa cộng các trường chỉ đọc; không có tọa độ, kênh Telegram hay mã cơ sở.</summary>
public sealed record ManagerBranchView(string Phone, string OpenTime, string CloseTime, int AbandonedThresholdMinutes,
    string Name, string Address, bool IsActive,
    string? BankAccountNo, string? BankCode, string? SepayAccountRef, QrProvider QrProvider = QrProvider.VietQR)
{
    public static ManagerBranchView From(Branch branch) => new(branch.Phone,
        branch.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture), branch.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        branch.AbandonedThresholdMinutes, branch.Name, branch.Address, branch.IsActive,
        branch.BankAccountNo, branch.BankCode, branch.SepayAccountRef, branch.QrProvider);
}
