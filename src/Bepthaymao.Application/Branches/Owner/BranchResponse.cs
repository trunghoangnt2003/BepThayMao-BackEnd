using System.Globalization;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.Owner;

/// <summary>Schema <c>Branch</c> của hợp đồng; không bao giờ chứa mật khẩu hay băm mật khẩu.</summary>
public sealed record BranchResponse(Guid Id, string Name, string Address, double Lat, double Lng, string Phone, bool IsActive,
    string OpenTime, string CloseTime, int AbandonedThresholdMinutes, IReadOnlyList<string> ZaloRecipientIds,
    string? BankAccountNo, string? BankCode, string? SepayAccountRef, string? ManagerIdentifier)
{
    public static BranchResponse From(Branch branch, string? managerIdentifier) => new(branch.Id, branch.Name, branch.Address,
        branch.Lat, branch.Lng, branch.Phone, branch.IsActive,
        branch.OpenTime.ToString("HH:mm", CultureInfo.InvariantCulture), branch.CloseTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        branch.AbandonedThresholdMinutes, [.. branch.ZaloRecipientIds], branch.BankAccountNo, branch.BankCode, branch.SepayAccountRef,
        managerIdentifier);
}
