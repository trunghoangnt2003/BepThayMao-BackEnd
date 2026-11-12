namespace Bepthaymao.Application.Abstractions.Security;

/// <summary>
/// Phạm vi dữ liệu của DbContext hiện tại. Không có Owner, cơ sở lẫn <see cref="IsSystem"/> nghĩa là
/// không thấy gì (fail-closed).
/// </summary>
public interface IDataScope
{
    /// <summary>Tác vụ tin cậy chạy trong tiến trình (seed, CLI) không gắn với yêu cầu HTTP.</summary>
    bool IsSystem { get; }
    Guid? OwnerId { get; }
    Guid? BranchId { get; }
}
