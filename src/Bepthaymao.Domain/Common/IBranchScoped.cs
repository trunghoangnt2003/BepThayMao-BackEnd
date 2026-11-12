namespace Bepthaymao.Domain.Common;

/// <summary>Entity thuộc một cơ sở; AppDbContext tự gắn named query filter <c>BranchScope</c>.</summary>
public interface IBranchScoped
{
    Guid BranchId { get; }
}
