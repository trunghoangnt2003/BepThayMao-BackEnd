namespace Bepthaymao.Application.Abstractions.Errors;

/// <summary>Xung đột trạng thái (ví dụ tên đăng nhập đã tồn tại); <see cref="Field"/> là khóa lỗi camelCase trong ProblemDetails.</summary>
public sealed class ConflictException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
