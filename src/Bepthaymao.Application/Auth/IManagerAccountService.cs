namespace Bepthaymao.Application.Auth;

public interface IManagerAccountService
{
    /// <summary>Tạo tài khoản BranchManager gắn một cơ sở; không tự mở hay commit transaction. Trả tên đăng nhập đã lưu.</summary>
    Task<string> CreateManagerAsync(Guid branchId, string identifier, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Đặt mật khẩu mới cho tài khoản quản lý của cơ sở và đổi security stamp; không tự mở hay commit transaction
    /// (handler bọc cùng việc thu hồi refresh token). Trả mã người dùng; ném <c>NotFoundException</c> khi cơ sở chưa có quản lý.
    /// </summary>
    Task<Guid> ResetPasswordAsync(Guid branchId, string newPassword, CancellationToken cancellationToken);
}
