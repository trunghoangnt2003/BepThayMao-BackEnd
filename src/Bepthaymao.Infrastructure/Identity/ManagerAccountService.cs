using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bepthaymao.Infrastructure.Identity;

internal sealed class ManagerAccountService(UserManager<AppUser> users, TimeProvider clock) : IManagerAccountService
{
    private const string _duplicateMessage = "Tên đăng nhập đã được sử dụng.";

    public async Task<string> CreateManagerAsync(Guid branchId, string identifier, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppUser user = new() { Id = Guid.CreateVersion7(), UserName = identifier, BranchId = branchId, CreatedAt = clock.GetUtcNow() };
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("manager.identifier", _duplicateMessage);
        }
        if (!created.Succeeded) throw Translate(created);
        IdentityResult assigned = await users.AddToRoleAsync(user, nameof(UserRole.BranchManager));
        if (!assigned.Succeeded)
            throw new InvalidOperationException("Không gán được vai trò quản lý: " + string.Join(", ", assigned.Errors.Select(error => error.Code)));
        return identifier;
    }

    public async Task<Guid> ResetPasswordAsync(Guid branchId, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppUser user = await users.Users.SingleOrDefaultAsync(value => value.BranchId == branchId, cancellationToken)
            ?? throw new NotFoundException();
        // Gỡ rồi đặt mật khẩu là hai lần cập nhật: handler bọc chúng trong một transaction nên lỗi giữa chừng rollback,
        // quản lý không bao giờ mất mật khẩu.
        IdentityResult removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded)
            throw new InvalidOperationException("Không gỡ được mật khẩu cũ: " + string.Join(", ", removed.Errors.Select(error => error.Code)));
        IdentityResult added = await users.AddPasswordAsync(user, newPassword);
        if (!added.Succeeded) throw TranslatePassword(added);
        IdentityResult stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
            throw new InvalidOperationException("Không đổi được security stamp: " + string.Join(", ", stamped.Errors.Select(error => error.Code)));
        return user.Id;
    }

    private static Exception TranslatePassword(IdentityResult result)
    {
        string[] codes = [.. result.Errors.Select(error => error.Code)];
        return codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal))
            ? new ValidationException(new Dictionary<string, string[]> { ["newPassword"] = ["Mật khẩu phải từ 10 đến 128 ký tự."] })
            : new InvalidOperationException("Không đặt được mật khẩu mới: " + string.Join(", ", codes));
    }

    private static Exception Translate(IdentityResult result)
    {
        string[] codes = [.. result.Errors.Select(error => error.Code)];
        if (codes.Any(code => code is nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new ConflictException("manager.identifier", _duplicateMessage);
        Dictionary<string, string[]> errors = [];
        if (codes.Any(code => code.StartsWith("Password", StringComparison.Ordinal)))
            errors["manager.password"] = ["Mật khẩu phải từ 10 đến 128 ký tự."];
        if (codes.Any(code => code is nameof(IdentityErrorDescriber.InvalidUserName)))
            errors["manager.identifier"] = ["Tên đăng nhập chỉ gồm chữ, số và các ký tự . _ @ + -"];
        return errors.Count > 0
            ? new ValidationException(errors)
            : new InvalidOperationException("Không thể tạo tài khoản quản lý: " + string.Join(", ", codes));
    }
}
