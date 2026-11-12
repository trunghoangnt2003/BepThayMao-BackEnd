using System.Globalization;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.Manager;

/// <summary>Schema <c>ManagerBranchView</c>: năm trường được sửa cộng các trường chỉ đọc; không có tọa độ hay mã cơ sở.</summary>
public sealed record ManagerBranchView(string Phone, string OpenTime, string CloseTime, int AbandonedThresholdMinutes,
    IReadOnlyList<string> ZaloRecipientIds, string Name, string Address, bool IsActive,
    string? BankAccountNo, string? BankCode, string? SepayAccountRef)
{
    public static ManagerBranchView From(Branch branch) => new(branch.Phone,
        branch.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture), branch.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        branch.AbandonedThresholdMinutes, [.. branch.ZaloRecipientIds], branch.Name, branch.Address, branch.IsActive,
        branch.BankAccountNo, branch.BankCode, branch.SepayAccountRef);
}
