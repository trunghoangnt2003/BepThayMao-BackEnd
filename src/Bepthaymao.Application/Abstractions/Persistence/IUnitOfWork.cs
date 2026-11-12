namespace Bepthaymao.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Chạy <paramref name="work"/> trong một transaction DB tường minh: thành công thì commit,
    /// lỗi thì rollback, bỏ mọi thực thể đang theo dõi rồi ném lại lỗi.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
