namespace Bepthaymao.Domain.Branches;

public sealed record BranchDetails(string Name, string Address, string Phone, double Lat, double Lng,
    TimeOnly OpenTime, TimeOnly CloseTime, int AbandonedThresholdMinutes,
    IReadOnlyList<string> ZaloRecipientIds, string? BankAccountNo, string? BankCode,
    string? SepayAccountRef, bool IsActive);
