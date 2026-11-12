namespace Bepthaymao.Application.Auth;

public interface IManagerAccountService
{
    /// <summary>Tạo tài khoản BranchManager gắn một cơ sở; không tự mở hay commit transaction. Trả tên đăng nhập đã lưu.</summary>
    Task<string> CreateManagerAsync(Guid branchId, string identifier, string password, CancellationToken cancellationToken);
}
